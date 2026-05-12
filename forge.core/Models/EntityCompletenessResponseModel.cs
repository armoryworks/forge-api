namespace Forge.Core.Models;

/// <summary>
/// Per-entity completeness breakdown returned by
/// <c>GET /api/v1/entities/{entityType}/{entityId}/completeness</c>. The
/// frontend chip + badge consume this directly. Only includes capabilities
/// that are currently enabled on this install (filtered server-side via
/// <c>ICapabilitySnapshotProvider</c>) — no false alarms about
/// requirements that don't apply.
///
/// Items where <see cref="EntityCompletenessCapability.Ok"/> is false count
/// as "incomplete for that capability". The chip surfaces the count;
/// clicking opens a popover that lists the failed
/// <see cref="EntityCompletenessMissingField"/>s grouped per capability.
/// </summary>
public record EntityCompletenessResponseModel(
    string EntityType,
    int EntityId,
    IReadOnlyList<EntityCompletenessCapability> Capabilities);

public record EntityCompletenessCapability(
    string CapabilityCode,
    string CapabilityName,
    bool Ok,
    IReadOnlyList<EntityCompletenessMissingField> MissingFields);

public record EntityCompletenessMissingField(
    string RequirementId,
    string DisplayNameKey,
    string MissingMessageKey);
