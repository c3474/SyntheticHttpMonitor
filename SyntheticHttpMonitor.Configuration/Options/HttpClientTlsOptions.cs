namespace SyntheticHttpMonitor.Options;

/// <summary>
/// Legacy global TLS switch (still bound from JSON). Prefer per-target
/// <see cref="TargetOptions.DangerousAcceptAnyServerCertificate"/> or <see cref="TargetDefaults.DangerousAcceptAnyServerCertificate"/>.
/// When true, applies to any target that does not set an explicit per-target value.
/// </summary>
public sealed class HttpClientTlsOptions
{
    /// <summary>
    /// When true, accepts any server TLS certificate for targets that omit an explicit per-target setting (OR with defaults).
    /// </summary>
    public bool DangerousAcceptAnyServerCertificate { get; set; }
}
