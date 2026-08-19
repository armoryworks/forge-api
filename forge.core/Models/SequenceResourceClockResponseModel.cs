using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceResourceClockResponseModel(
    int Id,
    string ResourceType,
    int ResourceId,
    DateTimeOffset ExpiresAt,
    SequenceExpiryAction ExpiryAction,
    string? EscalateRole,
    string? Note,
    DateTimeOffset? FiredAt,
    bool IsExpired);
