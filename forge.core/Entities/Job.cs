using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class Job : BaseAuditableEntity, IConcurrencyVersioned
{
    /// <summary>Optimistic-locking version. See IConcurrencyVersioned. WU-11.</summary>
    public uint Version { get; set; }

    public string JobNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int TrackTypeId { get; set; }
    public int CurrentStageId { get; set; }
    public int? AssigneeId { get; set; }
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    public int? CustomerId { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? CompletedDate { get; set; }
    public bool IsArchived { get; set; }
    public int BoardPosition { get; set; }
    public int? PartId { get; set; }
    public int? ParentJobId { get; set; }
    public int? SalesOrderLineId { get; set; }
    public int? MrpPlannedOrderId { get; set; }

    // Phase 3 H4 / WU-20 — pin the BOM revision the job was released
    // against, so historical reconstruction reads through Job →
    // BomRevision (specific id) rather than Job → Part → current BOM.
    // Captured by the Job-create / BOM-pin flow when the job is associated
    // with a part that has a current BOM revision.
    public int? BomRevisionIdAtRelease { get; set; }
    public BomRevision? BomRevisionAtRelease { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    // R&D iteration tracking
    public int IterationCount { get; set; }
    public string? IterationNotes { get; set; }

    // Internal projects
    public bool IsInternal { get; set; }
    public int? InternalProjectTypeId { get; set; }

    // Job Costing — Estimated
    public decimal EstimatedMaterialCost { get; set; }
    public decimal EstimatedLaborCost { get; set; }
    public decimal EstimatedBurdenCost { get; set; }
    public decimal EstimatedSubcontractCost { get; set; }
    public decimal EstimatedTotalCost => EstimatedMaterialCost + EstimatedLaborCost + EstimatedBurdenCost + EstimatedSubcontractCost;
    public decimal QuotedPrice { get; set; }
    public decimal EstimatedMarginPercent => QuotedPrice > 0 ? (QuotedPrice - EstimatedTotalCost) / QuotedPrice * 100 : 0;

    // Disposition
    public JobDisposition? Disposition { get; set; }
    public string? DispositionNotes { get; set; }
    public DateTimeOffset? DispositionAt { get; set; }

    // Custom fields (JSONB)
    public string? CustomFieldValues { get; set; }

    // Pro Services — engagement axis fields. Gated by CAP-PS-ENGAGEMENT at
    // the UI / API surface; columns are write-anytime. Per the G-17 spike
    // (docs/pro-services-rollout/phase-2-foundations/spike-01-engagement-
    // entity.md), engagements are Jobs on the Engagement track type, so
    // these fields live on Job rather than a separate Engagement entity.
    public int? EngagementTypeId { get; set; }       // FK → reference_data (group: engagement_type)
    public int? ProjectPhaseId { get; set; }         // FK → reference_data (group: project_phase)
    public BillingModelType? BillingModel { get; set; }  // T&M / FixedBid / Retainer
    public decimal? RetainerHours { get; set; }       // Hours purchased (Retainer model only)
    public decimal? RetainerBalanceHours { get; set; } // Hours remaining (debited by billable TimeEntries)
    public int? SowId { get; set; }                  // FK → quotes (SOW lives in Quote per spec)

    // Cover photo
    public int? CoverPhotoFileId { get; set; }
    public FileAttachment? CoverPhotoFile { get; set; }

    // Navigation
    public Part? Part { get; set; }
    public Job? ParentJob { get; set; }
    public ICollection<Job> ChildJobs { get; set; } = [];
    public TrackType TrackType { get; set; } = null!;
    public JobStage CurrentStage { get; set; } = null!;
    public Customer? Customer { get; set; }
    public ICollection<JobSubtask> Subtasks { get; set; } = [];
    public ICollection<JobActivityLog> ActivityLogs { get; set; } = [];
    public ICollection<PlanningCycleEntry> PlanningCycleEntries { get; set; } = [];
    public SalesOrderLine? SalesOrderLine { get; set; }
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = [];
    public ICollection<JobPart> JobParts { get; set; } = [];
    public MrpPlannedOrder? MrpPlannedOrder { get; set; }
    public ICollection<JobNote> Notes { get; set; } = [];
    public ICollection<MaterialIssue> MaterialIssues { get; set; } = [];
}
