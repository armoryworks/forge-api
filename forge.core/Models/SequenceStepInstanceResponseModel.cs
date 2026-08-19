using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Per-step marking. <paramref name="IsBlocked"/> is derived (predecessors satisfied, a gate not Go).</summary>
public record SequenceStepInstanceResponseModel(
    string StepKey,
    string Name,
    int SortOrder,
    SequenceStepStatus Status,
    bool IsBlocked,
    string? BlockedReason,
    IReadOnlyList<string> Predecessors,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? StartedAt,
    int? StartedByUserId,
    DateTimeOffset? CompletedAt,
    int? CompletedByUserId,
    string? SkipReason,
    DateTimeOffset? DwellExpiresAt,
    DateTimeOffset? DwellFiredAt);
