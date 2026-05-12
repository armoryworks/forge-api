using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models.Communications;
using Forge.Core.Settings;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1k.2 + phase 1m — production <see cref="IImapOAuthService"/>
/// backed by HttpClient against Google's and Microsoft's OAuth 2.0
/// token endpoints. Credentials read at request-time from
/// <see cref="ISettingsService"/>; admin populates them via the
/// /admin/settings UI rather than appsettings.json.
///
/// Email address discovery:
///   - Google returns <c>id_token</c> in the token response (when
///     openid scope is requested) AND/OR exposes a /userinfo endpoint.
///     We rely on the id_token's <c>email</c> claim for the simple
///     Gmail-only flow we ship; the openid scope was added to make
///     this possible without a second round-trip.
///   - Microsoft's id_token (v2.0 endpoint) carries <c>preferred_username</c>
///     which is the user's email.
///
/// All HTTP errors are caught + translated to InvalidOperationException
/// with a friendly message so the global ExceptionHandlingMiddleware
/// emits a 409 with the cause.
/// </summary>
public class ImapOAuthService(
    HttpClient http,
    ISettingsService settings,
    IClock clock,
    ILogger<ImapOAuthService> logger) : IImapOAuthService
{
    public async Task<bool> IsProviderConfiguredAsync(string providerKey, CancellationToken ct)
    {
        var (clientId, clientSecret) = await GetCredentialsAsync(providerKey, ct);
        var redirectUri = await settings.GetStringAsync(OAuthImapSettings.KeyRedirectUri, ct);
        return !string.IsNullOrEmpty(clientId)
            && !string.IsNullOrEmpty(clientSecret)
            && !string.IsNullOrEmpty(redirectUri);
    }

    public async Task<string> BuildAuthorizeUrlAsync(string providerKey, string state, CancellationToken ct)
    {
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {providerKey}");
        var (clientId, _) = await GetCredentialsAsync(providerKey, ct);
        var redirectUri = await settings.GetStringAsync(OAuthImapSettings.KeyRedirectUri, ct);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri))
        {
            throw new InvalidOperationException(
                $"OAuth credentials not configured for provider {providerKey}. "
                + "Set them under Admin → Settings → Email — OAuth.");
        }

        var scope = providerKey == "google"
            ? $"{provider.Scope} openid email profile"
            : provider.Scope;

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["client_id"] = clientId;
        qs["redirect_uri"] = redirectUri;
        qs["response_type"] = "code";
        qs["scope"] = scope;
        qs["state"] = state;
        qs["access_type"] = "offline";
        qs["prompt"] = "consent";
        return $"{provider.AuthorizeUrl}?{qs}";
    }

    public async Task<OAuthTokenResult> ExchangeCodeForTokensAsync(
        string providerKey, string code, CancellationToken ct)
    {
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {providerKey}");
        var (clientId, clientSecret) = await GetCredentialsAsync(providerKey, ct);
        var redirectUri = await settings.GetStringAsync(OAuthImapSettings.KeyRedirectUri, ct);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(redirectUri))
        {
            throw new InvalidOperationException(
                $"OAuth credentials not configured for provider {providerKey}.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
        };

        var response = await PostFormAsync(provider.TokenUrl, form, ct);
        var tokens = await ReadTokenResponseAsync(response, ct);

        if (string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new InvalidOperationException(
                "OAuth provider returned no refresh token. Try disconnecting any prior consent for this app and re-authorizing.");
        }

        var email = ExtractEmailFromIdToken(tokens.IdToken)
            ?? throw new InvalidOperationException(
                "OAuth provider returned no email claim. The connection can't be associated with a mailbox.");

        var expiresAt = clock.UtcNow.AddSeconds(tokens.ExpiresIn);
        return new OAuthTokenResult(tokens.AccessToken, tokens.RefreshToken, expiresAt, email);
    }

    public async Task<OAuthRefreshResult> RefreshAccessTokenAsync(
        string providerKey, string refreshToken, CancellationToken ct)
    {
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {providerKey}");
        var (clientId, clientSecret) = await GetCredentialsAsync(providerKey, ct);
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException(
                $"OAuth credentials not configured for provider {providerKey}.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        var response = await PostFormAsync(provider.TokenUrl, form, ct);
        var tokens = await ReadTokenResponseAsync(response, ct);

        if (string.IsNullOrEmpty(tokens.AccessToken))
        {
            throw new InvalidOperationException(
                "OAuth refresh failed — provider returned no access_token. The user may have revoked access.");
        }

        var expiresAt = clock.UtcNow.AddSeconds(tokens.ExpiresIn);
        return new OAuthRefreshResult(tokens.AccessToken, tokens.RefreshToken, expiresAt);
    }

    /// <summary>
    /// Pull (ClientId, ClientSecret) for the named provider out of
    /// settings. Returns (null, null) when unset; caller branches on
    /// the IsProviderConfigured check upstream.
    /// </summary>
    private async Task<(string? ClientId, string? ClientSecret)> GetCredentialsAsync(
        string providerKey, CancellationToken ct)
    {
        return providerKey?.ToLowerInvariant() switch
        {
            "google" => (
                await settings.GetStringAsync(OAuthImapSettings.KeyGoogleClientId, ct),
                await settings.GetStringAsync(OAuthImapSettings.KeyGoogleClientSecret, ct)),
            "microsoft" => (
                await settings.GetStringAsync(OAuthImapSettings.KeyMicrosoftClientId, ct),
                await settings.GetStringAsync(OAuthImapSettings.KeyMicrosoftClientSecret, ct)),
            _ => (null, null),
        };
    }

    private async Task<HttpResponseMessage> PostFormAsync(
        string url, IDictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("OAuth POST to {Url} failed ({Status}): {Body}", url, response.StatusCode, body);
                throw new InvalidOperationException(
                    $"OAuth provider returned {(int)response.StatusCode}: {Truncate(body, 200)}");
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Network error contacting OAuth provider: {ex.Message}");
        }
    }

    private static async Task<TokenResponse> ReadTokenResponseAsync(HttpResponseMessage resp, CancellationToken ct)
        => await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("OAuth provider returned an empty token response.");

    private static string? ExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
            return email;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    }
}
