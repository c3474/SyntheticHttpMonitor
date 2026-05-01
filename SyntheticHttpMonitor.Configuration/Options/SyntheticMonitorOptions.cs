namespace SyntheticHttpMonitor.Options;

public sealed class SyntheticMonitorOptions
{
    public const string SectionName = "SyntheticMonitor";

    public TargetDefaults Defaults { get; set; } = new();

    /// <summary>TLS / HTTP client behavior for probes.</summary>
    public HttpClientTlsOptions HttpClient { get; set; } = new();

    public List<TargetOptions> Targets { get; set; } = new();
}

public sealed class TargetDefaults
{
    public int IntervalSeconds { get; set; } = 60;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>If empty after merge, defaults to [200].</summary>
    public List<int> ExpectedStatusCodes { get; set; } = [200];

    public int MaxBodyBytes { get; set; } = 262_144;

    /// <summary>
    /// When a target omits <see cref="TargetOptions.DangerousAcceptAnyServerCertificate"/>, this value is combined with
    /// the legacy <see cref="HttpClientTlsOptions.DangerousAcceptAnyServerCertificate"/> (OR) to choose TLS validation behavior.
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate { get; set; }

    /// <summary>HTTP method sent for all probes unless overridden per target. Defaults to GET.</summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>Headers added to every probe request. Per-target Headers override on a per-key basis.</summary>
    public Dictionary<string, string>? Headers { get; set; }
}

public sealed class TargetOptions
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int? IntervalSeconds { get; set; }

    public int? TimeoutSeconds { get; set; }

    /// <summary>When null or empty after merge, <see cref="TargetDefaults.ExpectedStatusCodes"/> is used.</summary>
    public List<int>? ExpectedStatusCodes { get; set; }

    /// <summary>Optional; when null or whitespace, body content is not validated.</summary>
    public string? BodyRegex { get; set; }

    public int? MaxBodyBytes { get; set; }

    /// <summary>
    /// When null, TLS behavior comes from <see cref="TargetDefaults.DangerousAcceptAnyServerCertificate"/> OR the legacy
    /// global <see cref="HttpClientTlsOptions.DangerousAcceptAnyServerCertificate"/> (whichever enables bypass).
    /// </summary>
    public bool? DangerousAcceptAnyServerCertificate { get; set; }

    /// <summary>Overrides <see cref="TargetDefaults.HttpMethod"/> for this target. Null inherits the default.</summary>
    public string? HttpMethod { get; set; }

    /// <summary>Headers merged on top of <see cref="TargetDefaults.Headers"/>. Per-target values win on key conflicts.</summary>
    public Dictionary<string, string>? Headers { get; set; }

    public ResolvedTarget Resolve(TargetDefaults defaults, HttpClientTlsOptions legacyHttpClient)
    {
        var codes = ExpectedStatusCodes is { Count: > 0 }
            ? ExpectedStatusCodes
            : defaults.ExpectedStatusCodes;

        var dangerousTls = DangerousAcceptAnyServerCertificate
            ?? (legacyHttpClient.DangerousAcceptAnyServerCertificate || defaults.DangerousAcceptAnyServerCertificate);

        Dictionary<string, string>? mergedHeaders = null;
        if (defaults.Headers is { Count: > 0 } || Headers is { Count: > 0 })
        {
            mergedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (defaults.Headers is not null)
                foreach (var (k, v) in defaults.Headers)
                    mergedHeaders[k] = v;
            if (Headers is not null)
                foreach (var (k, v) in Headers)
                    mergedHeaders[k] = v;
        }

        return new ResolvedTarget(
            Name: string.IsNullOrWhiteSpace(Name) ? Url : Name,
            Url,
            IntervalSeconds: IntervalSeconds ?? defaults.IntervalSeconds,
            TimeoutSeconds: TimeoutSeconds ?? defaults.TimeoutSeconds,
            ExpectedStatusCodes: codes.Count > 0 ? codes : [200],
            BodyRegex: string.IsNullOrWhiteSpace(BodyRegex) ? null : BodyRegex,
            MaxBodyBytes: MaxBodyBytes ?? defaults.MaxBodyBytes,
            DangerousAcceptAnyServerCertificate: dangerousTls,
            HttpMethod: string.IsNullOrWhiteSpace(HttpMethod) ? defaults.HttpMethod : HttpMethod,
            Headers: mergedHeaders);
    }
}

public sealed record ResolvedTarget(
    string Name,
    string Url,
    int IntervalSeconds,
    int TimeoutSeconds,
    IReadOnlyList<int> ExpectedStatusCodes,
    string? BodyRegex,
    int MaxBodyBytes,
    bool DangerousAcceptAnyServerCertificate,
    string HttpMethod,
    IReadOnlyDictionary<string, string>? Headers);
