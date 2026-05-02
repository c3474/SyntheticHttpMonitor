namespace SyntheticHttpMonitor.Options;

public enum SmtpAuthMode
{
    /// <summary>Plain username and password via SMTP AUTH.</summary>
    Password,

    /// <summary>OAuth2 refresh-token flow via Google's token endpoint (Gmail SMTP).</summary>
    GmailOAuth2,

    /// <summary>OAuth2 refresh-token flow via Microsoft's token endpoint (Exchange Online / Microsoft 365).</summary>
    ExchangeOAuth2
}

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

    /// <summary>Authentication mode. Defaults to Password. Use GmailOAuth2 or ExchangeOAuth2 for modern auth.</summary>
    public SmtpAuthMode AuthMode { get; set; } = SmtpAuthMode.Password;

    /// <summary>Sender email address used for SMTP AUTH (required for all auth modes).</summary>
    public string? Username { get; set; }

    /// <summary>Password for Password auth mode. Not used for OAuth2 modes.</summary>
    public string? Password { get; set; }

    // OAuth2 fields — populate when AuthMode is GmailOAuth2 or ExchangeOAuth2.

    /// <summary>OAuth2 client ID from Google Cloud Console or Azure App Registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth2 refresh token from the one-time consent flow. Exchanged for access tokens at runtime.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Azure AD tenant ID (directory ID). Required for ExchangeOAuth2 only.</summary>
    public string TenantId { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public List<string> To { get; set; } = new();

    public List<string> Cc { get; set; } = new();

    public string SubjectDownPrefix { get; set; } = "[DOWN]";

    public string SubjectRecoveryPrefix { get; set; } = "[RECOVERED]";
}
