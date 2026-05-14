
namespace Forge.Core.Entities;

public class PpapSubmission : BaseAuditableEntity
{
    public string SubmissionNumber { get; set; } = string.Empty;
    public int PartId { get; set; }
    public int CustomerId { get; set; }
    public int PpapLevel { get; set; } = 3;
    public PpapStatus Status { get; set; } = PpapStatus.Draft;
    public PpapSubmissionReason Reason { get; set; }
    public string? PartRevision { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string? CustomerContactName { get; set; }
    public string? CustomerResponseNotes { get; set; }
    public string? InternalNotes { get; set; }
    public int? PswSignedByUserId { get; set; }
    public DateTimeOffset? PswSignedAt { get; set; }

    // Navigation (FK-only for ApplicationUser)
    public Part Part { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public ICollection<PpapElement> Elements { get; set; } = [];
}
