using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateNcrRequestModel
{
    public NcrType Type { get; init; }
    public int PartId { get; init; }
    public int? JobId { get; init; }
    public int? ProductionRunId { get; init; }
    public string? LotNumber { get; init; }
    public int? SalesOrderLineId { get; init; }
    public int? PurchaseOrderLineId { get; init; }
    public int? QcInspectionId { get; init; }
    public NcrDetectionStage DetectedAtStage { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal AffectedQuantity { get; init; }
    public decimal? DefectiveQuantity { get; init; }
    public string? ContainmentActions { get; init; }
    public int? CustomerId { get; init; }
    public int? VendorId { get; init; }
}
