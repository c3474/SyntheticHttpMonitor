namespace SyntheticHttpMonitor.Options;

/// <summary>Reserved for future ticket creation; not used in v1.</summary>
public sealed class TicketingApiOptions
{
    public const string SectionName = "TicketingApi";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>HTTP header name for the API key (e.g. X-API-Key).</summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>Prefer environment-specific secret store in production; placeholder for template.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional project or queue identifier required by your system.</summary>
    public string? ProjectKey { get; set; }
}
