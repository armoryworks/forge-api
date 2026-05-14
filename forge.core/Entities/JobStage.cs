
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
    public bool IsShopFloor { get; set; }
    public bool IsActive { get; set; } = true;

    public TrackType TrackType { get; set; } = null!;
    public ICollection<Job> Jobs { get; set; } = [];
}
