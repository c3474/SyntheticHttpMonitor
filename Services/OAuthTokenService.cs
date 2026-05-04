using System.Text.Json;
using SyntheticHttpMonitor.Options;

namespace SyntheticHttpMonitor.Services;

public sealed class OAuthTokenService
{
    public const string HttpClientName = nameof(OAuthTokenService);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OAuthTokenService> _logger;

    private string? _cachedToken;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OAuthTokenService(IHttpClientFactory httpClientFactory, ILogger<OAuthTokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(SmtpOptions options, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Refresh 2 minutes before expiry to avoid using a token that expires mid-send.
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresUtc.AddMinutes(-2))
                return _cachedToken;

            _logger.LogDebug("Refreshing OAuth2 access token for {AuthMode}", options.AuthMode);

            var (tokenUrl, content) = options.AuthMode switch
            {
                SmtpAuthMode.GmailOAuth2 => BuildGmailRequest(options),
                SmtpAuthMode.ExchangeOAuth2 => BuildExchangeRequest(options),
                SmtpAuthMode.OutlookOAuth2 => BuildOutlookRequest(options),
                _ => throw new InvalidOperationException($"AuthMode {options.AuthMode} is not an OAuth2 mode.")
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(tokenUrl, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"OAuth2 token refresh failed ({(int)response.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("OAuth2 token response missing access_token.");

            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _cachedToken = accessToken;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            _logger.LogInformation("OAuth2 access token refreshed; expires in {ExpiresIn}s", expiresIn);
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static (string tokenUrl, FormUrlEncodedContent content) BuildGmailRequest(SmtpOptions o) =>
        ("https://oauth2.googleapis.com/token",
         new FormUrlEncodedContent(new Dictionary<string, string>
         {
             ["client_id"] = o.ClientId,
             ["client_secret"] = o.ClientSecret,
             ["refresh_token"] = o.RefreshToken,
             ["grant_type"] = "refresh_token"
         }));

    private static (string tokenUrl, FormUrlEncodedContent content) BuildExchangeRequest(SmtpOptions o) =>
        ($"https://login.microsoftonline.com/{o.TenantId}/oauth2/v2.0/token",
         new FormUrlEncodedContent(new Dictionary<string, string>
         {
             ["client_id"] = o.ClientId,
             ["client_secret"] = o.ClientSecret,
             ["refresh_token"] = o.RefreshToken,
             ["grant_type"] = "refresh_token",
             ["scope"] = "https://outlook.office365.com/SMTP.Send offline_access"
         }));

    // Outlook.com personal accounts use the shared "consumers" tenant — no TenantId needed.
    private static (string tokenUrl, FormUrlEncodedContent content) BuildOutlookRequest(SmtpOptions o) =>
        ("https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
         new FormUrlEncodedContent(new Dictionary<string, string>
         {
             ["client_id"] = o.ClientId,
             ["client_secret"] = o.ClientSecret,
             ["refresh_token"] = o.RefreshToken,
             ["grant_type"] = "refresh_token",
             ["scope"] = "https://outlook.office365.com/SMTP.Send offline_access"
         }));
}
