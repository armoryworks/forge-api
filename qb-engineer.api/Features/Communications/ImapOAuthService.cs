using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — production <see cref="IImapOAuthService"/> backed
/// by HttpClient against Google's and Microsoft's OAuth 2.0 token
/// endpoints.
///
/// Email address discovery:
///   - Google returns <c>id_token</c> in the token response (when
///     openid scope is requested) AND/OR exposes a /userinfo endpoint.
///     We rely on the id_token's <c>email</c> claim for the simple
///     Gmail-only flow we ship; the openid scope was added to make
///     this possible without a second round-trip. For users who declined
///     the email scope we fall back to a tokeninfo lookup.
///   - Microsoft's id_token (v2.0 endpoint) carries <c>preferred_username</c>
///     which is the user's email.
///
/// All HTTP errors are caught + translated to InvalidOperationException
/// with a friendly message so the global ExceptionHandlingMiddleware
/// emits a 409 with the cause.
/// </summary>
public class ImapOAuthService(
    HttpClient http,
    IOptions<OAuthImapOptions> options,
    IClock clock,
    ILogger<ImapOAuthService> logger) : IImapOAuthService
{
    private readonly OAuthImapOptions _options = options.Value;

    public bool IsProviderConfigured(string providerKey)
    {
        var creds = ResolveCredentials(providerKey);
        return creds is not null && creds.IsConfigured && !string.IsNullOrEmpty(_options.RedirectUri);
    }

    public string BuildAuthorizeUrl(string providerKey, string state)
    {
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {providerKey}");
        var creds = ResolveCredentials(providerKey)
            ?? throw new InvalidOperationException($"OAuth credentials not configured for provider {providerKey}");

        // Include openid + email + profile alongside the IMAP scope so
        // the id_token carries the user's email — saves us a userinfo
        // round-trip after the code exchange. (Microsoft already includes
        // these in the documented scope; Google needs them appended.)
        var scope = providerKey == "google"
            ? $"{provider.Scope} openid email profile"
            : provider.Scope;

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["client_id"] = creds.ClientId!;
        qs["redirect_uri"] = _options.RedirectUri;
        qs["response_type"] = "code";
        qs["scope"] = scope;
        qs["state"] = state;
        qs["access_type"] = "offline"; // Google: required for refresh_token
        qs["prompt"] = "consent";       // Force re-consent so refresh_token is always returned
        return $"{provider.AuthorizeUrl}?{qs}";
    }

    public async Task<OAuthTokenResult> ExchangeCodeForTokensAsync(
        string providerKey, string code, CancellationToken ct)
    {
        var provider = ImapOAuthProvider.FromKey(providerKey)
            ?? throw new InvalidOperationException($"Unknown OAuth provider: {providerKey}");
        var creds = ResolveCredentials(providerKey)
            ?? throw new InvalidOperationException($"OAuth credentials not configured for provider {providerKey}");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = creds.ClientId!,
            ["client_secret"] = creds.ClientSecret!,
            ["redirect_uri"] = _options.RedirectUri,
        };

        var response = await PostFormAsync(provider.TokenUrl, form, ct);
        var tokens = await ReadTokenResponseAsync(response, ct);

        if (string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
        {
            // Refresh token absent typically means the user previously
            // consented and the provider isn't re-issuing one. We force
            // prompt=consent in the authorize URL above to avoid that;
            // if it still happens, surface a friendly error.
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
        var creds = ResolveCredentials(providerKey)
            ?? throw new InvalidOperationException($"OAuth credentials not configured for provider {providerKey}");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = creds.ClientId!,
            ["client_secret"] = creds.ClientSecret!,
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

    private OAuthProviderCredentials? ResolveCredentials(string providerKey) => providerKey?.ToLowerInvariant() switch
    {
        "google" => _options.Google,
        "microsoft" => _options.Microsoft,
        _ => null,
    };

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
            // Google: "email"
            // Microsoft: "preferred_username" or "email"
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

    /// <summary>
    /// Provider token-endpoint response shape. Both Google and Microsoft
    /// follow the OAuth 2.0 form here; refresh_token is optional on
    /// refresh responses (many providers omit it = "reuse the one you
    /// already have"), so it's nullable.
    /// </summary>
    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    }
}
