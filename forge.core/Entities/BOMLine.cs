using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class BOMLine : BaseAuditableEntity
{
    public int ParentPartId { get; set; }
    public int ChildPartId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? ReferenceDesignator { get; set; }
    public int SortOrder { get; set; }
    public BOMSourceType SourceType { get; set; } = BOMSourceType.Buy;
    public int? LeadTimeDays { get; set; }
    public string? Notes { get; set; }

    public int? UomId { get; set; }
    public int? VendorId { get; set; }

    public UnitOfMeasure? Uom { get; set; }
    public Vendor? Vendor { get; set; }
    public Part ParentPart { get; set; } = null!;
    public Part ChildPart { get; set; } = null!;
}
