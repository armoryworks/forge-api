using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Step of a sequence definition (request and response shape).</summary>
public record SequenceStepDefinitionModel(
    string Key,
    string Name,
    string? Description,
    int SortOrder,
    SequenceJoinPolicy JoinPolicy = SequenceJoinPolicy.All,
    int? MaxDwellMinutes = null,
    SequenceExpiryAction DwellExpiryAction = SequenceExpiryAction.Flag,
    string? EscalateRole = null);
