
namespace Forge.Core.Models;

public record NcrResponseModel
{
    public int Id { get; init; }
    public string NcrNumber { get; init; } = string.Empty;
    public NcrType Type { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public int? JobId { get; init; }
    public string? JobNumber { get; init; }
    public int? ProductionRunId { get; init; }
    public string? LotNumber { get; init; }
    public int? SalesOrderLineId { get; init; }
    public int? PurchaseOrderLineId { get; init; }
    public int? QcInspectionId { get; init; }
    public int DetectedById { get; init; }
    public string DetectedByName { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; }
    public NcrDetectionStage DetectedAtStage { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal AffectedQuantity { get; init; }
    public decimal? DefectiveQuantity { get; init; }
    public string? ContainmentActions { get; init; }
    public int? ContainmentById { get; init; }
    public string? ContainmentByName { get; init; }
    public DateTimeOffset? ContainmentAt { get; init; }
    public NcrDispositionCode? DispositionCode { get; init; }
    public int? DispositionById { get; init; }
    public string? DispositionByName { get; init; }
    public DateTimeOffset? DispositionAt { get; init; }
    public string? DispositionNotes { get; init; }
    public string? ReworkInstructions { get; init; }
    public decimal? MaterialCost { get; init; }
    public decimal? LaborCost { get; init; }
    public decimal? TotalCostImpact { get; init; }
    public NcrStatus Status { get; init; }
    public int? CapaId { get; init; }
    public string? CapaNumber { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? VendorId { get; init; }
    public string? VendorName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
