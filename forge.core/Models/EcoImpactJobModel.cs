namespace Forge.Core.Models;

/// <summary>An open job that builds the affected part (<c>Direct</c>) or an assembly whose BOM uses it (<c>WhereUsed</c>, with the parent part).</summary>
public record EcoImpactJobModel(
    int JobId,
    string JobNumber,
    string Title,
    int PartId,
    string? PartNumber,
    string? PartRevision,
    string? StageName,
    DateTimeOffset? DueDate,
    string Relation);
