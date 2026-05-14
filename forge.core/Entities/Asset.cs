using Forge.Core.Interfaces;

namespace Forge.Core.Entities;

public class Asset : BaseAuditableEntity, IActiveAware
{
    public string Name { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public string? Location { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public string? PhotoFileId { get; set; }
    public decimal CurrentHours { get; set; }
    public string? Notes { get; set; }

    // Tooling-specific fields
    public bool IsCustomerOwned { get; set; }
    public int? CavityCount { get; set; }
    public int? ToolLifeExpectancy { get; set; }
    public int CurrentShotCount { get; set; }
    public int? SourceJobId { get; set; }
    public Job? SourceJob { get; set; }
    public int? SourcePartId { get; set; }
    public Part? SourcePart { get; set; }

    // Acquisition / depreciation — Phase 3 F4. Captured at create-time so the
    // small-shop onboarding form does not need a PATCH-after-create round trip.
    // Nullable: null = unknown / not yet entered.
    public decimal? AcquisitionCost { get; set; }
    public DepreciationMethod? DepreciationMethod { get; set; }

    // Local linkage to a work center when the asset is associated with a station.
    public int? WorkCenterId { get; set; }
    public WorkCenter? WorkCenter { get; set; }

    // External-accounting GL account. Optional on the local DTO — populated by
    // the accounting-sync integration when one is connected. Phase 3 F4.
    public string? GlAccount { get; set; }

    // IActiveAware — Phase 3 H2 active-check. Assets that are Retired or
    // OutOfService cannot have new maintenance schedules created against them.
    public bool IsActiveForNewTransactions => Status != AssetStatus.Retired && Status != AssetStatus.OutOfService;
    public string GetDisplayName() => Name;
}
