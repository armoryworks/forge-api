using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class JobStage : BaseAuditableEntity
{
    public int TrackTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Color { get; set; } = "#94a3b8";
    public int? WIPLimit { get; set; }
    public AccountingDocumentType? AccountingDocumentType { get; set; }
    public bool IsIrreversible { get; set; }

    /// <summary>
    /// A mandatory stage cannot be skipped by a forward move: a job moving
    /// forward past this stage's SortOrder must currently sit at or beyond it.
    /// Enforced by MoveJobStageHandler / BulkMoveJobStageHandler. Dispose /
    /// cancel-style flows are unaffected (they don't move through stages).
    /// </summary>
    public bool IsMandatory { get; set; }
    public bool IsShopFloor { get; set; }
    public bool IsActive { get; set; } = true;

    public TrackType TrackType { get; set; } = null!;
    public ICollection<Job> Jobs { get; set; } = [];
}
