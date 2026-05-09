using QBEngineer.Core.Models.Communications;

namespace QBEngineer.Api.Features.Communications;

/// <summary>
/// Wave 8 phase 1k.2 — OAuth-IMAP token broker. Encapsulates the three
/// HTTP interactions with Google / Microsoft:
///   1. <see cref="BuildAuthorizeUrl"/> — assemble the consent URL the
///      user gets redirected to. Pure URL construction; no I/O.
///   2. <see cref="ExchangeCodeForTokensAsync"/> — POST the auth code +
///      client creds to the provider's token endpoint, get back access /
///      refresh tokens + email address.
///   3. <see cref="RefreshAccessTokenAsync"/> — POST the refresh_token
///      to the same endpoint, get a fresh access_token. Called from the
///      IMAP adapter just before connect when the access_token is expired
///      or near-expiry.
///
/// Real impl uses HttpClient. Tests inject a fake that returns canned
/// JSON so the handshake logic can be exercised without registering an
/// app with Google/Microsoft.
/// </summary>
public interface IImapOAuthService
{
    /// <summary>True when the install has both ClientId and ClientSecret
    /// configured for the named provider.</summary>
    bool IsProviderConfigured(string providerKey);

    /// <summary>
    /// Build the provider's authorize URL. State token is generated +
    /// persisted by the caller (the begin-handler) and threaded in here
    /// — this method is pure URL construction, no I/O.
    /// </summary>
    string BuildAuthorizeUrl(string providerKey, string state);

    Task<OAuthTokenResult> ExchangeCodeForTokensAsync(
        string providerKey, string code, CancellationToken ct);

    Task<OAuthRefreshResult> RefreshAccessTokenAsync(
        string providerKey, string refreshToken, CancellationToken ct);
}

/// <summary>
/// Result of the initial code-for-token exchange. Includes the email
/// address (Google: returned in a separate userinfo lookup; Microsoft:
/// already in the id_token claims) so the connection row's
/// ExternalAccountId can be populated without a second round-trip.
/// </summary>
public sealed record OAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string EmailAddress);

/// <summary>Result of a refresh. Refresh-token rotation: some providers
/// rotate the refresh_token on every refresh; if NewRefreshToken is
/// non-null, the caller persists it.</summary>
public sealed record OAuthRefreshResult(
    string AccessToken,
    string? NewRefreshToken,
    DateTimeOffset AccessTokenExpiresAt);
