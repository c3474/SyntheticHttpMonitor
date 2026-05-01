using System.Text;
using System.Text.RegularExpressions;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.Services;

public sealed class HttpProbe
{
    /// <summary>Validates server TLS certificates (default).</summary>
    public const string HttpClientNameStrict = nameof(HttpProbe);

    /// <summary>Does not validate server TLS certificates (use only when explicitly configured per target).</summary>
    public const string HttpClientNameInsecure = nameof(HttpProbe) + ":Insecure";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpProbe> _logger;

    public HttpProbe(IHttpClientFactory httpClientFactory, ILogger<HttpProbe> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ProbeResult> ProbeAsync(ResolvedTarget target, Regex? bodyRegex, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return ProbeResult.Fail($"Invalid or unsupported URL: {target.Url}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, target.TimeoutSeconds)));

        var clientName = target.DangerousAcceptAnyServerCertificate ? HttpClientNameInsecure : HttpClientNameStrict;
        var client = _httpClientFactory.CreateClient(clientName);
        client.Timeout = Timeout.InfiniteTimeSpan;

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(target.HttpMethod), uri);
            if (target.Headers is not null)
                foreach (var (name, value) in target.Headers)
                    request.Headers.TryAddWithoutValidation(name, value);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            var status = (int)response.StatusCode;
            if (!target.ExpectedStatusCodes.Contains(status))
            {
                return ProbeResult.Fail(
                    $"HTTP {status} ({response.StatusCode}); expected one of: {string.Join(", ", target.ExpectedStatusCodes)}");
            }

            if (bodyRegex is null)
            {
                return ProbeResult.Ok();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var body = await ReadBodyLimitedAsync(stream, target.MaxBodyBytes, cts.Token);

            try
            {
                if (!bodyRegex.IsMatch(body))
                {
                    return ProbeResult.Fail("Response body did not match the configured regular expression.");
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "Regex match timed out for {Name}", target.Name);
                return ProbeResult.Fail("Body regex evaluation timed out.");
            }

            return ProbeResult.Ok();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Probe request failed for {Name} ({Url})", target.Name, target.Url);
            return ProbeResult.Fail($"Request failed: {ex.Message}");
        }
    }

    private static async Task<string> ReadBodyLimitedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[8192];
        await using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 65_536));
        var total = 0;
        while (total < maxBytes)
        {
            var toRead = Math.Min(buffer.Length, maxBytes - total);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0)
            {
                break;
            }

            ms.Write(buffer, 0, read);
            total += read;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

public readonly record struct ProbeResult(bool Success, string? ErrorMessage)
{
    public static ProbeResult Ok() => new(true, null);

    public static ProbeResult Fail(string message) => new(false, message);
}
