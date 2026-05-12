using Microsoft.AspNetCore.Authentication;

namespace Forge.Api.Authentication;

/// <summary>
/// Authentication options for the BI API-key scheme.
///
/// The scheme accepts plaintext keys via:
///   - <c>X-Api-Key: &lt;key&gt;</c> header (preferred)
///   - <c>Authorization: ApiKey &lt;key&gt;</c> header (alternate)
///
/// No fallback to query string (security anti-pattern; query strings are
/// frequently logged by load balancers and reverse proxies).
///
/// Phase 3 / WU-04 / A3.
/// </summary>
public class BiApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "BiApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string AuthorizationScheme = "ApiKey";

    /// <summary>
    /// Whether to emit a <c>BiApiKeyUsed</c> system-wide audit row on every
    /// successful key authentication. May be too noisy at high request rates;
    /// off by default. Toggle via configuration:
    /// <c>BiApiKey:AuditUseEvents = true</c>.
    /// </summary>
    public bool AuditUseEvents { get; set; } = false;
}
