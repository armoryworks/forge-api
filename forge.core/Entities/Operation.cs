namespace Forge.Core.Entities;

public class Operation : BaseAuditableEntity
{
    public int PartId { get; set; }
    public int StepNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public int? WorkCenterId { get; set; }
    public int? AssetId { get; set; }
    public int? EstimatedMinutes { get; set; }
    public bool IsQcCheckpoint { get; set; }
    public string? QcCriteria { get; set; }
    public int? ReferencedOperationId { get; set; }

    // Scheduling fields
    public decimal SetupMinutes { get; set; }
    public decimal RunMinutesEach { get; set; }
    public decimal RunMinutesLot { get; set; }
    public decimal OverlapPercent { get; set; }
    public decimal ScrapFactor { get; set; }
    public bool IsSubcontract { get; set; }
    public int? SubcontractVendorId { get; set; }
    public decimal? SubcontractCost { get; set; }
    public int? SubcontractLeadTimeDays { get; set; }
    public string? SubcontractInstructions { get; set; }

    // Phase 3 H5 / WU-13 — subcontract metadata exposed on routing-op DTOs.
    // SubcontractTurnTimeDays is decimal (half-day granularity, e.g. 0.5,
    // 1.5, 5.0) — distinct from SubcontractLeadTimeDays (int, internal
    // scheduling lead). The TODO calls for "turn time" semantics on the
    // routing-op contract (round-trip days for the subcontracted batch),
    // separate from the integer lead-time bookkeeping field.
    public decimal? SubcontractTurnTimeDays { get; set; }

    // Costing fields
    public decimal LaborRate { get; set; }
    public decimal BurdenRate { get; set; }
    public decimal EstimatedLaborCost { get; set; }
    public decimal EstimatedBurdenCost { get; set; }

    public Part Part { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }
    public Asset? Asset { get; set; }
    public Vendor? SubcontractVendor { get; set; }
    public Operation? ReferencedOperation { get; set; }
    public ICollection<OperationMaterial> Materials { get; set; } = [];
}
