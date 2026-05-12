namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Audit event vocabulary for capability mutations.
/// Phase A wires the constants only; the actual audit-write calls land in
/// Phase C when the mutation surface goes live (per 4D §2.5 / §7).
///
/// Per 4D-decisions-log #4: <c>entityType</c> is the literal string
/// <c>"Capability"</c> and <c>entityId</c> is the capability code (passed
/// to <see cref="Forge.Api.Services.ISystemAuditWriter"/> via the
/// <c>details</c> JSON payload, since the writer's <c>entityId</c> is an
/// <see cref="int"/>; the <see cref="Core.Entities.Capability.Id"/> is the
/// integer used for that field, with <c>Code</c> in <c>details</c>).
/// </summary>
public static class CapabilityAuditEvents
{
    /// <summary>Audit row <c>entity_type</c> for every capability mutation.</summary>
    public const string EntityType = "Capability";

    /// <summary>Action code: a capability was enabled.</summary>
    public const string Enabled = "CapabilityEnabled";

    /// <summary>Action code: a capability was disabled.</summary>
    public const string Disabled = "CapabilityDisabled";

    /// <summary>Action code: a capability's config payload was changed.</summary>
    public const string ConfigChanged = "CapabilityConfigChanged";

    /// <summary>Action code: a 4B preset was applied (atomic multi-capability flip).</summary>
    public const string PresetApplied = "PresetApplied";
}
