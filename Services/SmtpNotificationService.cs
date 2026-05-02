using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.Services;

public sealed class SmtpNotificationService
{
    private readonly SmtpOptions _options;
    private readonly OAuthTokenService _oAuth;
    private readonly ILogger<SmtpNotificationService> _logger;

    public SmtpNotificationService(IOptions<SmtpOptions> options, OAuthTokenService oAuth, ILogger<SmtpNotificationService> logger)
    {
        _options = options.Value;
        _oAuth = oAuth;
        _logger = logger;
    }

    public async Task SendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("SMTP disabled; skipping send for subject {Subject}", subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("SMTP enabled but Host is empty; cannot send.");
            return;
        }

        if (_options.To.Count == 0)
        {
            _logger.LogWarning("SMTP enabled but no recipients; cannot send.");
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        foreach (var to in _options.To.Where(static x => !string.IsNullOrWhiteSpace(x)))
            message.To.Add(MailboxAddress.Parse(to.Trim()));
        foreach (var cc in _options.Cc.Where(static x => !string.IsNullOrWhiteSpace(x)))
            message.Cc.Add(MailboxAddress.Parse(cc.Trim()));

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, ResolveSecureSocketOptions(_options), cancellationToken);
            await AuthenticateAsync(client, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            _logger.LogInformation("Sent SMTP alert: {Subject}", subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMTP alert: {Subject}", subject);
        }
    }

    private async Task AuthenticateAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        switch (_options.AuthMode)
        {
            case SmtpAuthMode.Password:
                if (!string.IsNullOrWhiteSpace(_options.Username))
                    await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
                break;

            case SmtpAuthMode.GmailOAuth2:
            case SmtpAuthMode.ExchangeOAuth2:
                if (string.IsNullOrWhiteSpace(_options.Username))
                    throw new InvalidOperationException(
                        $"Smtp:Username (sender email address) is required for {_options.AuthMode}.");

                var accessToken = await _oAuth.GetAccessTokenAsync(_options, cancellationToken);
                await client.AuthenticateAsync(new SaslMechanismOAuth2(_options.Username, accessToken), cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unknown SmtpAuthMode: {_options.AuthMode}");
        }
    }

    private static SecureSocketOptions ResolveSecureSocketOptions(SmtpOptions o)
    {
        if (o.UseSsl)
            return SecureSocketOptions.SslOnConnect;
        return o.UseStartTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None;
    }
}
