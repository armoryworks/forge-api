using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Gate of a sequence definition (request and response shape). <paramref name="ConfigJson"/> shape depends on <paramref name="SourceType"/>.</summary>
public record SequenceGateDefinitionModel(
    string StepKey,
    string Key,
    string Name,
    SequenceGateSourceType SourceType,
    string ConfigJson = "{}",
    SequenceExpiryAction ExpiryAction = SequenceExpiryAction.Block,
    string? EscalateRole = null);
