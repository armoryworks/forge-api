namespace Forge.Api.Services;

/// <summary>
/// Validates an OIDC <c>id_token</c> issued by an external identity provider
/// and returns its trusted claims (subject, email, optional hosted-domain
/// hint). Used by the SSO token-exchange endpoint so federated clients can
/// trade an external id_token for a Forge JWT without going through the
/// browser-based OAuth code flow.
/// </summary>
public interface IExternalIdTokenValidator
{
    /// <summary>
    /// Validate a Google-issued id_token against Google's published JWKS
    /// and the configured Google client id (audience). Throws
    /// <see cref="System.Security.Authentication.AuthenticationException"/>
    /// when validation fails for any reason (bad signature, expired, wrong
    /// audience / issuer, etc.).
    /// </summary>
    Task<ExternalIdTokenClaims> ValidateGoogleAsync(string idToken, CancellationToken ct);
}

/// <summary>Trusted claims extracted from a validated external id_token.</summary>
/// <param name="Subject">
/// The provider's stable subject identifier (<c>sub</c> claim). Maps to
/// <see cref="Forge.Data.Context.ApplicationUser.GoogleId"/> etc.
/// </param>
/// <param name="Email">
/// The user's email per the <c>email</c> claim.
/// </param>
/// <param name="HostedDomain">
/// Google's <c>hd</c> claim — the Google Workspace hosted-domain hint, if
/// present. Independent of email domain (a user can have an email at one
/// domain but be a member of a different Workspace tenant). Null for
/// personal Google accounts.
/// </param>
public record ExternalIdTokenClaims(string Subject, string Email, string? HostedDomain);
