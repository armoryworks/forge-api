using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceEventResponseModel(
    int Id,
    SequenceEventType Type,
    string? StepKey,
    string? GateKey,
    string? PayloadJson,
    DateTimeOffset OccurredAt,
    int? ActorUserId);
