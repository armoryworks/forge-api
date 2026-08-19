using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SequenceDefinitionResponseModel(
    int Id,
    string Code,
    int Version,
    string Name,
    string? Description,
    string? SubjectEntityType,
    SequenceDefinitionStatus Status,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<SequenceStepDefinitionModel> Steps,
    IReadOnlyList<SequenceEdgeDefinitionModel> Edges,
    IReadOnlyList<SequenceGateDefinitionModel> Gates,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
