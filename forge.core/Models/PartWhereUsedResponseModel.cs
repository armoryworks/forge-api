using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// One parent part whose current BOM lists the queried part, with how many open work
/// orders are building that parent.
/// </summary>
public record PartWhereUsedResponseModel(
    int BomLineId,
    int ParentPartId,
    string ParentPartNumber,
    string ParentName,
    string ParentRevision,
    decimal QuantityPer,
    BOMSourceType SourceType,
    int OpenWorkOrderCount);
