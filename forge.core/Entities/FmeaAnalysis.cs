using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class FmeaAnalysis : BaseAuditableEntity
{
    public string FmeaNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FmeaType Type { get; set; }
    public int? PartId { get; set; }
    public int? OperationId { get; set; }
    public FmeaStatus Status { get; set; } = FmeaStatus.Draft;
    public string? PreparedBy { get; set; }
    public string? Responsibility { get; set; }
    public DateOnly? OriginalDate { get; set; }
    public DateOnly? RevisionDate { get; set; }
    public int RevisionNumber { get; set; } = 1;
    public string? Notes { get; set; }
    public int? PpapSubmissionId { get; set; }

    // Navigation
    public Part? Part { get; set; }
    public Operation? Operation { get; set; }
    public PpapSubmission? PpapSubmission { get; set; }
    public ICollection<FmeaItem> Items { get; set; } = [];
}
