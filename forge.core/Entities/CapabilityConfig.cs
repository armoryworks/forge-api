namespace Forge.Core.Entities;

/// <summary>
/// Phase 4 Phase-A — Stores tunable config payload for a single capability.
/// 1:0..1 with <see cref="Capability"/>. The payload is stored as raw JSON
/// (jsonb in Postgres) — typed parsing happens per-feature when the capability
/// uses it (Phase A keeps it untyped per implementation-decisions doc).
///
/// Phase 4 Phase-C — Implements <see cref="IConcurrencyVersioned"/> so the
/// PUT /config endpoint can use If-Match / 412. The Version on this entity
/// is independent of Capability.Version (config edits don't bump capability
/// row version; toggles don't bump config row version).
/// </summary>
public class CapabilityConfig : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>FK to <see cref="Capability"/>. Also serves as the natural unique key (one config per capability).</summary>
    public int CapabilityId { get; set; }
    public Capability? Capability { get; set; }

    /// <summary>JSON payload — raw text, parsed by capability-specific consumers. Empty object "{}" by default.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>Schema version — increment when a capability's config shape changes (per 4D §2.1 §8.4).</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Optimistic concurrency token (Postgres xmin / EF Core RowVersion).</summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Phase 4 Phase-C — API-surfaced monotonic Version (ETag value). Bumped
    /// in <see cref="Forge.Data.Context.AppDbContext"/> on every Modified save.
    /// </summary>
    public uint Version { get; set; } = 1;
}
