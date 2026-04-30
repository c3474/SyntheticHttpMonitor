namespace SyntheticHttpMonitor.Options;

/// <summary>PagerDuty Events API v2 (generic inbound integration routing key).</summary>
public sealed class PagerDutyOptions
{
    public const string SectionName = "PagerDuty";

    public bool Enabled { get; set; }

    /// <summary>Events API integration key (same as routing_key in the enqueue payload).</summary>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>POST URL; default is PagerDuty global Events API.</summary>
    public string EventsApiUrl { get; set; } = "https://events.pagerduty.com/v2/enqueue";

    /// <summary>One of: critical, error, warning, info.</summary>
    public string Severity { get; set; } = "critical";

    /// <summary>Shown as alert source in PagerDuty. Empty uses the machine name.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Send event_action resolve when a target recovers (same dedup_key as trigger).</summary>
    public bool ResolveOnRecovery { get; set; } = true;
}
