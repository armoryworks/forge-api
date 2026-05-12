namespace Forge.Core.Entities;

/// <summary>
/// Pro Services rollout (Artifact 4 §3.4 / D9) — per-entity link to a
/// folder on a specific cloud-storage provider. Hybrid storage: one
/// install can have multiple providers active simultaneously, with each
/// entity binding to one of them.
///
/// <para>Polymorphic via <see cref="EntityType"/> + <see cref="EntityId"/>
/// (no FK on EntityId — application layer enforces validity). Common
/// entity types: <c>"Job"</c>, <c>"Customer"</c>, <c>"Quote"</c>,
/// <c>"Project"</c>, <c>"Deliverable"</c>.</para>
/// </summary>
public class EntityCloudLink : BaseAuditableEntity
{
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    public int ProviderId { get; set; }

    /// <summary>Provider-side folder identifier.</summary>
    public string FolderExternalId { get; set; } = string.Empty;

    /// <summary>Human-readable path of the folder on the provider — cached for display.</summary>
    public string? FolderPath { get; set; }

    /// <summary>Direct URL to open the folder in the provider's web UI.</summary>
    public string? FolderUrl { get; set; }

    public Guid? CreatedByUserId { get; set; }

    /// <summary>One of: <c>"preset_apply"</c>, <c>"manual"</c>, <c>"auto_create"</c>.</summary>
    public string CreatedVia { get; set; } = "manual";

    public CloudStorageProvider Provider { get; set; } = null!;
}
