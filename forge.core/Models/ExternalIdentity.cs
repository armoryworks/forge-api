namespace Forge.Core.Models;

/// <summary>
/// One external-system identity grant — OAuth tokens + supporting metadata.
/// Provider-agnostic shape used by every Forge-to-third-party integration
/// (QuickBooks, Xero, Google Drive, Google Calendar, Gmail-OAuth, etc.).
///
/// Two storage scopes (see <see cref="TokenScope"/>):
///   - <b>Install</b> — one row in <c>system_settings</c> per provider.
///     Used for shared resources like QuickBooks (one company's books for
///     the whole install) or a Forge-as-author Drive connection.
///   - <b>User</b> — one row per (user, provider) pair in
///     <c>UserCloudStorageLink</c> (for storage providers) or
///     <c>UserIntegration</c> (everything else). Used when the audit
///     trail must attribute the operation to a specific user.
///
/// The resolver picks which scope to use based on
/// <see cref="TokenResolutionPolicy"/> at the call site.
/// </summary>
public record ExternalIdentityToken(
    /// <summary>OAuth access token (cleartext — caller is responsible for not logging).</summary>
    string AccessToken,
    /// <summary>OAuth refresh token, when the provider issues one.</summary>
    string? RefreshToken,
    /// <summary>Access-token expiry. Null means "no expiry tracked".</summary>
    DateTimeOffset? AccessTokenExpiresAt,
    /// <summary>Refresh-token expiry (some providers, e.g. QuickBooks, rotate refresh tokens too).</summary>
    DateTimeOffset? RefreshTokenExpiresAt,
    /// <summary>
    /// Provider-side user identifier from the OAuth handshake. Examples:
    /// Google user email, Microsoft Graph user id, Dropbox account id.
    /// Useful for cross-referencing token rows to the external account
    /// they grant.
    /// </summary>
    string? ExternalUserId,
    /// <summary>
    /// Provider-side organization / tenant / realm identifier. Examples:
    /// QuickBooks RealmId, Xero TenantId, Zoho OrganizationId. Null when
    /// the provider has no concept of org/tenant on top of a user identity.
    /// </summary>
    string? RealmOrTenantId,
    /// <summary>
    /// Provider-specific extras the resolver doesn't model directly
    /// (scopes granted, id_token JWT, etc.). Stored verbatim alongside
    /// the token; callers can inspect for provider-specific quirks.
    /// </summary>
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Result of an <c>IExternalIdentityResolver.ResolveAsync</c> call. Carries
/// the access token plus enough metadata for the caller to know which
/// scope was used (so audit / activity rows can record "acted as user N"
/// vs "acted as the install identity").
/// </summary>
public record ResolvedExternalIdentity(
    string AccessToken,
    string Provider,
    TokenScope ScopeUsed,
    /// <summary>Set only when <c>ScopeUsed == User</c>.</summary>
    int? UserId,
    string? RealmOrTenantId);

/// <summary>Token storage scope.</summary>
public enum TokenScope
{
    /// <summary>Install-wide: one connection for the whole Forge instance.</summary>
    Install,
    /// <summary>Per-user: each user has their own connection.</summary>
    User,
}

/// <summary>
/// How the resolver should reconcile a call site's user context against
/// the install-wide / per-user storage layers. The right policy depends
/// on what the OPERATION's audit trail needs to show.
/// </summary>
public enum TokenResolutionPolicy
{
    /// <summary>
    /// Use the user's token if connected; fall back to install-wide.
    /// Sensible default for unattributed-but-attributable operations
    /// (e.g. background indexing of an active user's folders).
    /// </summary>
    PreferUser,
    /// <summary>
    /// Use the user's token; fail if not connected. Required when the
    /// operation MUST attribute to the user — e.g. sending email from
    /// the user's mailbox (a system-identity fallback would put the
    /// wrong From: address on the message).
    /// </summary>
    RequireUser,
    /// <summary>
    /// Ignore any user token; use install-wide only. For system reflexes
    /// like auto-creating engagement folders in a Shared Drive, where
    /// the operation isn't a user's manual action and Forge itself is
    /// the author of record.
    /// </summary>
    RequireInstall,
    /// <summary>
    /// Provider only supports install-wide tokens (e.g. QuickBooks — one
    /// company, one connection). Resolver returns the install token if
    /// connected, fails otherwise. UserId is ignored.
    /// </summary>
    InstallOnly,
}
