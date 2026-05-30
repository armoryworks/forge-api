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
}
