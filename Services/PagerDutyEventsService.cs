using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.Services;

public sealed class PagerDutyEventsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<PagerDutyOptions> _options;
    private readonly ILogger<PagerDutyEventsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public PagerDutyEventsService(
        IHttpClientFactory httpClientFactory,
        IOptions<PagerDutyOptions> options,
        ILogger<PagerDutyEventsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task TriggerDownAsync(ResolvedTarget target, string errorDetail, CancellationToken cancellationToken)
    {
        var o = _options.Value;
        if (!o.Enabled || string.IsNullOrWhiteSpace(o.RoutingKey))
        {
            return;
        }

        var dedup = BuildDedupKey(target.Name);
        var summary = BuildSummary(target, errorDetail);
        var source = string.IsNullOrWhiteSpace(o.Source) ? Environment.MachineName : o.Source.Trim();
        var severity = NormalizeSeverity(o.Severity);

        var body = new PagerDutyEnqueueBody(
            RoutingKey: o.RoutingKey.Trim(),
            EventAction: "trigger",
            DedupKey: dedup,
            Payload: new PagerDutyPayload(summary, severity, source));

        await PostJsonAsync(o.EventsApiUrl, body, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResolveAsync(string targetName, CancellationToken cancellationToken)
    {
        var o = _options.Value;
        if (!o.Enabled || !o.ResolveOnRecovery || string.IsNullOrWhiteSpace(o.RoutingKey))
        {
            return;
        }

        var dedup = BuildDedupKey(targetName);
        var body = new PagerDutyResolveBody(
            RoutingKey: o.RoutingKey.Trim(),
            EventAction: "resolve",
            DedupKey: dedup);

        await PostJsonAsync(o.EventsApiUrl, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostJsonAsync<T>(string url, T body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var client = _httpClientFactory.CreateClient(nameof(PagerDutyEventsService));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("PagerDuty Events API accepted request ({Status})", (int)response.StatusCode);
            return;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogWarning(
            "PagerDuty Events API returned {Status}: {Body}",
            (int)response.StatusCode,
            text.Length > 512 ? text[..512] + "…" : text);
    }

    internal static string BuildDedupKey(string targetName)
    {
        var segment = SanitizeForDedup(targetName);
        var raw = $"synthetic-monitor:{segment}";
        return raw.Length <= 255 ? raw : raw[..255];
    }

    private static string SanitizeForDedup(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "unknown";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ':')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.Length > 0 ? sb.ToString() : "target";
    }

    private static string BuildSummary(ResolvedTarget target, string errorDetail)
    {
        var name = string.IsNullOrWhiteSpace(target.Name) ? target.Url : target.Name;
        var detail = string.IsNullOrWhiteSpace(errorDetail) ? "check failed" : errorDetail.Trim();
        var summary = $"{name} — {target.Url}: {detail}";
        return summary.Length <= 1024 ? summary : summary[..1021] + "…";
    }

    private static string NormalizeSeverity(string? s)
    {
        var v = (s ?? "critical").Trim().ToLowerInvariant();
        return v is "critical" or "error" or "warning" or "info" ? v : "critical";
    }
}

internal sealed record PagerDutyPayload(string Summary, string Severity, string Source);

internal sealed record PagerDutyEnqueueBody(
    string RoutingKey,
    string EventAction,
    string DedupKey,
    PagerDutyPayload Payload);

internal sealed record PagerDutyResolveBody(string RoutingKey, string EventAction, string DedupKey);
