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
}
