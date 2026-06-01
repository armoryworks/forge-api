using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateBOMLineRequestModel(
    int ChildPartId,
    decimal Quantity,
    string? ReferenceDesignator,
    BOMSourceType SourceType,
    int? LeadTimeDays,
    string? Notes,
    // UoM purchase-units effort — the UoM this component is consumed in (default = child's stock UoM).
    int? UomId = null);
