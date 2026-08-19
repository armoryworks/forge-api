using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceGateInstanceResponseModel(
    string StepKey,
    string GateKey,
    string Name,
    SequenceGateSourceType SourceType,
    SequenceGateVerdict Verdict,
    string? Reason,
    DateTimeOffset? LastEvaluatedAt,
    DateTimeOffset? ClearedAt,
    int? ClearedByUserId,
    DateTimeOffset? OverriddenAt,
    int? OverriddenByUserId,
    string? OverrideReason);
