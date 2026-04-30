namespace SyntheticHttpMonitor.Options;

public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    /// <summary>Consecutive failed checks before treating target as down.</summary>
    public int FailureThreshold { get; set; } = 2;

    /// <summary>
    /// While still down, repeat alert at this interval (minutes). 0 = only the first down alert per incident.
    /// </summary>
    public int RepeatWhileDownMinutes { get; set; } = 0;

    public bool SendRecoveryEmail { get; set; } = true;
}
