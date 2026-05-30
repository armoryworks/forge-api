using Forge.Core.Enums;

namespace Forge.Core.Models;

public record BOMEntryResponseModel(
    int Id,
    int ChildPartId,
    string ChildPartNumber,
    string ChildName,
    decimal Quantity,
    string? ReferenceDesignator,
    int SortOrder,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? Notes,
    int? UomId,
    string? UomCode,
    string? UomLabel);
