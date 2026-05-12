using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class NonConformance : BaseAuditableEntity
{
    public string NcrNumber { get; set; } = string.Empty;
    public NcrType Type { get; set; }
    public int PartId { get; set; }
    public int? JobId { get; set; }
    public int? ProductionRunId { get; set; }
    public string? LotNumber { get; set; }
    public int? SalesOrderLineId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public int? QcInspectionId { get; set; }

    // Detection
    public int DetectedById { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public NcrDetectionStage DetectedAtStage { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal AffectedQuantity { get; set; }
    public decimal? DefectiveQuantity { get; set; }

    // Containment
    public string? ContainmentActions { get; set; }
    public int? ContainmentById { get; set; }
    public DateTimeOffset? ContainmentAt { get; set; }

    // Disposition
    public NcrDispositionCode? DispositionCode { get; set; }
    public int? DispositionById { get; set; }
    public DateTimeOffset? DispositionAt { get; set; }
    public string? DispositionNotes { get; set; }
    public string? ReworkInstructions { get; set; }

    // Cost
    public decimal? MaterialCost { get; set; }
    public decimal? LaborCost { get; set; }
    public decimal? TotalCostImpact { get; set; }

    // Status & links
    public NcrStatus Status { get; set; } = NcrStatus.Open;
    public int? CapaId { get; set; }
    public int? CustomerId { get; set; }
    public int? VendorId { get; set; }

    // Navigation (no ApplicationUser nav properties — FK-only pattern)
    public Part Part { get; set; } = null!;
    public Job? Job { get; set; }
    public CorrectiveAction? Capa { get; set; }
    public Customer? Customer { get; set; }
    public Vendor? Vendor { get; set; }
}
