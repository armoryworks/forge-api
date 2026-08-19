using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceInstanceResponseModel(
    int Id,
    int DefinitionId,
    string DefinitionCode,
    int DefinitionVersion,
    string DefinitionName,
    string? SubjectEntityType,
    int? SubjectEntityId,
    SequenceInstanceStatus Status,
    DateTimeOffset StartedAt,
    int? StartedByUserId,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    uint Version,
    IReadOnlyList<SequenceStepInstanceResponseModel> Steps,
    IReadOnlyList<SequenceGateInstanceResponseModel> Gates);
