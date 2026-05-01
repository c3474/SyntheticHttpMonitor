using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.Services;

public sealed class MonitorHostedService : BackgroundService
{
    private readonly IOptions<SyntheticMonitorOptions> _monitorOptions;
    private readonly IOptions<AlertingOptions> _alertingOptions;
    private readonly IOptions<SmtpOptions> _smtpOptions;
    private readonly IOptions<TicketingApiOptions> _ticketingOptions;
    private readonly IOptions<PagerDutyOptions> _pagerDutyOptions;
    private readonly HttpProbe _probe;
    private readonly SmtpNotificationService _smtp;
    private readonly PagerDutyEventsService _pagerDuty;
    private readonly ILogger<MonitorHostedService> _logger;

    private readonly ConcurrentDictionary<string, TargetRuntimeState> _state = new();

    public MonitorHostedService(
        IOptions<SyntheticMonitorOptions> monitorOptions,
        IOptions<AlertingOptions> alertingOptions,
        IOptions<SmtpOptions> smtpOptions,
        IOptions<TicketingApiOptions> ticketingOptions,
        IOptions<PagerDutyOptions> pagerDutyOptions,
        HttpProbe probe,
        SmtpNotificationService smtp,
        PagerDutyEventsService pagerDuty,
        ILogger<MonitorHostedService> logger)
    {
        _monitorOptions = monitorOptions;
        _alertingOptions = alertingOptions;
        _smtpOptions = smtpOptions;
        _ticketingOptions = ticketingOptions;
        _pagerDutyOptions = pagerDutyOptions;
        _probe = probe;
        _smtp = smtp;
        _pagerDuty = pagerDuty;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var ticketing = _ticketingOptions.Value;
        if (ticketing.Enabled)
        {
            _logger.LogWarning(
                "TicketingApi:Enabled is true but API integration is not implemented yet; no tickets will be created.");
        }

        var monitorOpts = _monitorOptions.Value;
        var tlsBypassTargets = monitorOpts.Targets
            .Select(t => t.Resolve(monitorOpts.Defaults, monitorOpts.HttpClient))
            .Where(r => r.DangerousAcceptAnyServerCertificate)
            .Select(r => r.Name)
            .ToList();
        if (tlsBypassTargets.Count > 0)
        {
            _logger.LogWarning(
                "HTTPS certificate validation is disabled for {Count} target(s) ({Targets}). MITM risk — use only where intentional.",
                tlsBypassTargets.Count,
                string.Join(", ", tlsBypassTargets));
        }

        ValidateConfiguration();
        LogStartupBanner(monitorOpts);
        await base.StartAsync(cancellationToken);
    }

    private void LogStartupBanner(SyntheticMonitorOptions monitorOpts)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var ver = string.IsNullOrWhiteSpace(vi.ProductVersion)
                    ? (vi.FileVersion?.Trim() ?? "?")
                    : vi.ProductVersion.Trim();
                _logger.LogInformation(
                    "Synthetic HTTP Monitor starting: version {Version}, exe {Exe}, working directory {Cwd}, configured targets {TargetCount}",
                    ver,
                    path,
                    Environment.CurrentDirectory,
                    monitorOpts.Targets.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Synthetic HTTP Monitor starting: working directory {Cwd}, configured targets {TargetCount}",
                    Environment.CurrentDirectory,
                    monitorOpts.Targets.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Startup banner logging skipped");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _monitorOptions.Value;
        if (options.Targets.Count == 0)
        {
            _logger.LogWarning("No targets configured under SyntheticMonitor:Targets; service is idle.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        var tasks = options.Targets.Select(t => RunTargetAsync(t, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private void ValidateConfiguration()
    {
        var options = _monitorOptions.Value;
        var defaults = options.Defaults;
        var legacyTls = options.HttpClient;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in options.Targets)
        {
            var resolved = target.Resolve(defaults, legacyTls);
            if (!names.Add(resolved.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate target name '{resolved.Name}'. Each target must have a unique Name (or Url-derived name).");
            }
            if (string.IsNullOrWhiteSpace(resolved.Url))
            {
                throw new InvalidOperationException($"Target '{resolved.Name}' has an empty Url.");
            }

            if (!Uri.TryCreate(resolved.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"Target '{resolved.Name}' must use an http or https URL.");
            }

            if (resolved.IntervalSeconds < 5)
            {
                throw new InvalidOperationException($"Target '{resolved.Name}' IntervalSeconds must be at least 5.");
            }

            if (string.IsNullOrWhiteSpace(resolved.HttpMethod))
            {
                throw new InvalidOperationException($"Target '{resolved.Name}' HttpMethod must not be empty.");
            }

            if (resolved.BodyRegex is not null)
            {
                _ = new Regex(resolved.BodyRegex, RegexOptions.None, TimeSpan.FromSeconds(2));
            }
        }

        var pd = _pagerDutyOptions.Value;
        if (!pd.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(pd.RoutingKey))
        {
            throw new InvalidOperationException(
                "PagerDuty:Enabled is true but PagerDuty:RoutingKey is empty. Set the Events API integration routing key or disable PagerDuty.");
        }

        if (string.IsNullOrWhiteSpace(pd.EventsApiUrl)
            || !Uri.TryCreate(pd.EventsApiUrl.Trim(), UriKind.Absolute, out var eventsUri)
            || eventsUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "PagerDuty:EventsApiUrl must be a non-empty absolute http or https URL when PagerDuty is enabled.");
        }
    }

    private async Task RunTargetAsync(TargetOptions targetOptions, CancellationToken stoppingToken)
    {
        var opts = _monitorOptions.Value;
        var target = targetOptions.Resolve(opts.Defaults, opts.HttpClient);
        Regex? bodyRegex = null;
        if (target.BodyRegex is not null)
        {
            bodyRegex = new Regex(target.BodyRegex, RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        }

        _logger.LogInformation(
            "Starting monitor loop for {Name} ({Url}) every {Interval}s",
            target.Name,
            target.Url,
            target.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _probe.ProbeAsync(target, bodyRegex, stoppingToken);
                await HandleProbeOutcomeAsync(target, result, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error probing {Name}", target.Name);
                await HandleProbeOutcomeAsync(
                    target,
                    ProbeResult.Fail($"Internal error: {ex.Message}"),
                    stoppingToken);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(target.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleProbeOutcomeAsync(ResolvedTarget target, ProbeResult result, CancellationToken ct)
    {
        var alerting = _alertingOptions.Value;
        var smtp = _smtpOptions.Value;
        var pagerDuty = _pagerDutyOptions.Value;
        var threshold = Math.Max(1, alerting.FailureThreshold);
        var repeatMinutes = Math.Max(0, alerting.RepeatWhileDownMinutes);

        var state = _state.GetOrAdd(target.Name, _ => new TargetRuntimeState());

        if (result.Success)
        {
            state.ConsecutiveFailures = 0;
            if (state.ReportedDown)
            {
                state.ReportedDown = false;
                state.LastAlertUtc = null;
                _logger.LogInformation("Target {Name} is healthy again.", target.Name);
                if (alerting.SendRecoveryEmail)
                {
                    var subject = $"{smtp.SubjectRecoveryPrefix} {target.Name}";
                    var body = BuildRecoveryBody(target);
                    await _smtp.SendAsync(subject, body, ct);
                }

                if (pagerDuty.ResolveOnRecovery)
                {
                    try
                    {
                        await _pagerDuty.ResolveAsync(target.Name, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "PagerDuty resolve failed for {Name}", target.Name);
                    }
                }
            }

            return;
        }

        state.ConsecutiveFailures++;
        _logger.LogWarning(
            "Probe failed for {Name} ({Url}): {Error} (consecutive failures: {Count})",
            target.Name,
            target.Url,
            result.ErrorMessage,
            state.ConsecutiveFailures);

        if (state.ConsecutiveFailures < threshold)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!state.ReportedDown)
        {
            state.ReportedDown = true;
            state.LastAlertUtc = now;
            await SendDownAlertAsync(target, result.ErrorMessage ?? "Unknown error", smtp, ct);
            return;
        }

        if (repeatMinutes <= 0 || state.LastAlertUtc is null)
        {
            return;
        }

        var next = state.LastAlertUtc.Value.AddMinutes(repeatMinutes);
        if (now < next)
        {
            return;
        }

        state.LastAlertUtc = now;
        await SendDownAlertAsync(target, result.ErrorMessage ?? "Unknown error (repeat)", smtp, ct);
    }

    private async Task SendDownAlertAsync(ResolvedTarget target, string detail, SmtpOptions smtp, CancellationToken ct)
    {
        var subject = $"{smtp.SubjectDownPrefix} {target.Name}";
        var body = BuildDownBody(target, detail);
        await _smtp.SendAsync(subject, body, ct);

        try
        {
            await _pagerDuty.TriggerDownAsync(target, detail, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PagerDuty trigger failed for {Name}", target.Name);
        }
    }

    private static string BuildDownBody(ResolvedTarget target, string detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target: {target.Name}");
        sb.AppendLine($"URL: {target.Url}");
        sb.AppendLine($"Time (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine($"Detail: {detail}");
        return sb.ToString();
    }

    private static string BuildRecoveryBody(ResolvedTarget target)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Target: {target.Name}");
        sb.AppendLine($"URL: {target.Url}");
        sb.AppendLine($"Time (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine("Status: probe succeeded; target is considered healthy again.");
        return sb.ToString();
    }

    private sealed class TargetRuntimeState
    {
        public int ConsecutiveFailures { get; set; }

        public bool ReportedDown { get; set; }

        public DateTime? LastAlertUtc { get; set; }
    }
}
