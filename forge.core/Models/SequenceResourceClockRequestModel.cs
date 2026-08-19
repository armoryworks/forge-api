using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceResourceClockRequestModel(
    string ResourceType,
    int ResourceId,
    DateTimeOffset ExpiresAt,
    SequenceExpiryAction ExpiryAction = SequenceExpiryAction.Block,
    string? EscalateRole = null,
    string? Note = null);
