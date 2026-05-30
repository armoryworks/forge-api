namespace Forge.Core.Models;

public class SsoProviderOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Authority { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional per-install email-domain allow-list. When empty (or null),
    /// any account the provider authenticates is eligible for local-user
    /// matching. When non-empty, the email-domain (suffix after '@') of the
    /// validated external account MUST match one of the listed domains, or
    /// the callback handler rejects with 403 "domain not permitted".
    ///
    /// Comparison is case-insensitive. Sub-domains are NOT auto-included
    /// (e.g. <c>"example.com"</c> permits <c>user@example.com</c> but not
    /// <c>user@sub.example.com</c>). Wildcards are not supported — list
    /// each permitted domain explicitly.
    ///
    /// Enforced uniformly by <c>SsoCallbackHandler</c>, so both the
    /// browser-OAuth callback and the token-exchange endpoint honor the
    /// same policy. See <c>docs/api-key-integrations.md</c>.
    /// </summary>
    public List<string>? AllowedDomains { get; set; }

    /// <summary>
    /// Additional audience values accepted by the token-exchange endpoint's
    /// id_token validator. The provider's primary <see cref="ClientId"/> is
    /// always accepted; this list permits id_tokens minted under a related
    /// OAuth client — e.g. a federated app (Tuyere) that has its own
    /// Google/Microsoft OAuth client but shares the same end-user identity
    /// model with Forge. Without this, a Tuyere-issued id_token would be
    /// rejected because its <c>aud</c> claim is Tuyere's client id, not
    /// Forge's.
    ///
    /// Browser-flow OAuth (the <c>SsoCallback</c> path) is unaffected —
    /// that flow always uses Forge's own client id and never sees
    /// external audiences.
    /// </summary>
    public List<string>? AdditionalAudiences { get; set; }

    /// <summary>
    /// Microsoft-only allow-list of tenant ids (the <c>tid</c> claim on an
    /// Azure AD v2.0 id_token, a GUID). When non-empty, the
    /// <c>token-exchange</c> handler accepts id_tokens only from the listed
    /// tenants — this lets an install be multi-tenant (no
    /// <c>Authority</c> override needed) while still restricting which
    /// tenants are trusted, without forcing single-tenant mode.
    ///
    /// Use cases:
    ///   - A managed-service provider runs Forge for 5 customers, each
    ///     on their own Microsoft 365 tenant — list all 5 tenant guids.
    ///   - You want to permit your tenant + your partner's tenant.
    ///
    /// When empty (or null) on a multi-tenant deployment, any Microsoft
    /// tenant whose id_token's audience matches Forge's client id is
    /// accepted (the audience check already gates this hard — see
    /// <c>ExternalIdTokenValidator.ValidateMicrosoftAsync</c>). Ignored by
    /// non-Microsoft providers (Google has no tenant concept; generic OIDC
    /// uses the discovery doc's issuer directly).
    /// </summary>
    public List<string>? AllowedTenantIds { get; set; }
}
