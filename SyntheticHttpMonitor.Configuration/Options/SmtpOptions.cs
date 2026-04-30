namespace SyntheticHttpMonitor.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 25;

    /// <summary>Implicit TLS (SMTPS), typically port 465.</summary>
    public bool UseSsl { get; set; }

    /// <summary>STARTTLS after connect, typical on port 25/587.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = string.Empty;

    public List<string> To { get; set; } = new();

    public List<string> Cc { get; set; } = new();

    public string SubjectDownPrefix { get; set; } = "[DOWN]";

    public string SubjectRecoveryPrefix { get; set; } = "[RECOVERED]";
}
