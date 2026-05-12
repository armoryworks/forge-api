using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class MaterialIssue : BaseAuditableEntity
{
    public int JobId { get; set; }
    public int PartId { get; set; }
    public int? OperationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost => Quantity * UnitCost;
    public int IssuedById { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public int? BinContentId { get; set; }
    public int? StorageLocationId { get; set; }
    public string? LotNumber { get; set; }
    public MaterialIssueType IssueType { get; set; } = MaterialIssueType.Issue;
    public int? ReturnReasonId { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Job Job { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public Operation? Operation { get; set; }
    public BinContent? BinContent { get; set; }
    public StorageLocation? StorageLocation { get; set; }
}
