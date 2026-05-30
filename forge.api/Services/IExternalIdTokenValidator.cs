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
    /// and the configured Google client id (plus any
    /// <c>Sso:Google:AdditionalAudiences</c>). Throws
    /// <see cref="System.Security.Authentication.AuthenticationException"/>
    /// when validation fails for any reason (bad signature, expired, wrong
    /// audience / issuer, unverified email, etc.).
    /// </summary>
    Task<ExternalIdTokenClaims> ValidateGoogleAsync(string idToken, CancellationToken ct);

    /// <summary>
    /// Validate a Microsoft-issued (Azure AD v2.0) id_token. Multi-tenant by
    /// default (any <c>https://login.microsoftonline.com/{tenant-id}/v2.0</c>
    /// issuer is accepted); set <c>Sso:Microsoft:Authority</c> to a tenant-
    /// specific authority to lock the install to one tenant. Audience is
    /// validated against <c>Sso:Microsoft:ClientId</c> plus any
    /// <c>Sso:Microsoft:AdditionalAudiences</c>. Subject is derived from
    /// <c>oid</c> when present (tenant-stable AAD object id) so federated
    /// clients with a different OAuth client id still match the same user;
    /// falls back to <c>sub</c> for personal accounts.
    /// </summary>
    Task<ExternalIdTokenClaims> ValidateMicrosoftAsync(string idToken, CancellationToken ct);

    /// <summary>
    /// Validate an id_token issued by a generic OIDC provider configured at
    /// <c>Sso:Oidc:Authority</c>. JWKS, issuer, and signing keys come from
    /// the authority's discovery document. Audience is validated against
    /// <c>Sso:Oidc:ClientId</c> plus any <c>Sso:Oidc:AdditionalAudiences</c>.
    /// If the token carries <c>email_verified</c>, it must be <c>true</c>;
    /// when absent, the email is trusted (some IdPs omit the claim).
    /// </summary>
    Task<ExternalIdTokenClaims> ValidateOidcAsync(string idToken, CancellationToken ct);
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
