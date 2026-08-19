namespace Forge.Core.Models;

/// <summary>Create/update payload for a draft sequence definition — the whole graph in one document.</summary>
public record SequenceDefinitionRequestModel(
    string Code,
    string Name,
    string? Description,
    string? SubjectEntityType,
    IReadOnlyList<SequenceStepDefinitionModel> Steps,
    IReadOnlyList<SequenceEdgeDefinitionModel> Edges,
    IReadOnlyList<SequenceGateDefinitionModel> Gates);
