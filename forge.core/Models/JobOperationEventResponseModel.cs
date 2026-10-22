using Forge.Core.Enums;

namespace Forge.Core.Models;

public record JobOperationEventResponseModel(
    int Id,
    JobOperationEventKind Kind,
    decimal Quantity,
    string? ReasonCode,
    int UserId,
    string UserName,
    DateTimeOffset OccurredAt,
    bool GateOverride = false,
    string? GateOverrideReason = null);
