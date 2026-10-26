using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One row of an indented where-used: a parent part whose current BOM lists the part one level
/// below it on <see cref="Path"/>, with how many open work orders are building that parent.
/// <see cref="Level"/> 1 is a direct parent; higher levels are the assemblies those parents go
/// into. <see cref="Path"/> runs from the queried part up to and including this parent.
/// </summary>
public record PartWhereUsedResponseModel(
    int BomLineId,
    int ParentPartId,
    string ParentPartNumber,
    string ParentName,
    string ParentRevision,
    decimal QuantityPer,
    BOMSourceType SourceType,
    int OpenWorkOrderCount,
    int Level,
    IReadOnlyList<string> Path);
