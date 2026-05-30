using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One row in the Connections registry — the federated admin view over every
/// credential / connection an install has issued or accepted (BI keys,
/// system keys, EDI trading partners, QuickBooks OAuth, communication sync
/// configs, cloud-storage links).
///
/// <para>Rows are synthesized at query time by <c>IConnectionsRegistry</c>
/// — there is no <c>connections</c> table. Each native surface keeps its
/// own table + admin page; the registry is a read-only federation layer.</para>
///
/// <para><b>Id is composite</b> — <see cref="Kind"/> + <see cref="SourceId"/>
/// uniquely identifies a row across all sources. The UI uses
/// <see cref="ManageRoute"/> to deep-link to the native management
/// surface; it does NOT issue mutations against the registry.</para>
/// </summary>
public record IntegrationRecordResponseModel
{
    /// <summary>Discriminator. Drives the kind chip + manage-route shape on the UI.</summary>
    public IntegrationKind Kind { get; init; }

    /// <summary>Native id of the underlying entity (e.g. <c>SystemApiKey.Id</c>
    /// as a string). Strings because some sources are singletons keyed by a
    /// well-known name (QuickBooks OAuth) rather than an integer row id.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>Human-readable label for the row (key name, partner code, etc.).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Bound user email for user-scoped rows (system keys, cloud-storage
    /// links, communications), null for install-level / unbound rows
    /// (BI keys, QuickBooks OAuth, EDI partners).
    /// </summary>
    public string? OwnerEmail { get; init; }

    /// <summary>
    /// Short status string — Active / Revoked / Expired / Connected /
    /// Disconnected. UI-friendly free-form; not an enum because each source
    /// has its own status vocabulary.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Last time the integration was actually exercised (null when
    /// never used or when the source doesn't track usage).</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>When the connection was issued. Null for sources that don't
    /// carry a creation timestamp (e.g. <see cref="IntegrationKind.QuickBooksOAuth"/>
    /// — persisted as a flat <c>SystemSetting</c> blob).</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Client-side route the UI navigates to when the operator clicks
    /// "Manage" on this row — points at the native admin surface for the
    /// source (e.g. <c>/admin/system-api-keys</c>, <c>/admin/edi/42</c>).
    /// </summary>
    public string ManageRoute { get; init; } = string.Empty;
}
