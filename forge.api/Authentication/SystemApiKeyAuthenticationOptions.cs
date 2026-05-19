using Microsoft.AspNetCore.Authentication;

namespace Forge.Api.Authentication;

/// <summary>
/// Authentication options for the system (user-bound) API-key scheme.
/// See <c>docs/api-key-integrations.md</c>.
///
/// The scheme accepts plaintext keys via:
///   - <c>X-Forge-Api-Key: &lt;key&gt;</c> header (preferred — namespaced so
///     a host that also speaks the BI key scheme on <c>X-Api-Key</c> can
///     route both without collision).
///   - <c>Authorization: ForgeApiKey &lt;key&gt;</c> header (alternate).
///
/// No fallback to query string (security anti-pattern; query strings are
/// frequently logged by load balancers and reverse proxies).
///
/// Distinct from <see cref="BiApiKeyAuthenticationOptions"/>: this scheme
/// authenticates AS the bound user (principal carries the user's id and
/// real role grants), while the BI scheme builds a synthetic principal
/// with a "BiApiClient" role and key-id NameIdentifier.
/// </summary>
public class SystemApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "SystemApiKey";
    public const string HeaderName = "X-Forge-Api-Key";
    public const string AuthorizationScheme = "ForgeApiKey";

    /// <summary>
    /// Whether to emit a <c>SystemApiKeyUsed</c> system-wide audit row on
    /// every successful key authentication. May be too noisy at high
    /// request rates; off by default. Toggle via configuration:
    /// <c>SystemApiKey:AuditUseEvents = true</c>.
    /// </summary>
    public bool AuditUseEvents { get; set; }
}
