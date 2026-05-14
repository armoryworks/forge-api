
namespace Forge.Core.Models;

public record PpapSubmissionResponseModel
{
    public int Id { get; init; }
    public string SubmissionNumber { get; init; } = string.Empty;
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public int PpapLevel { get; init; }
    public PpapStatus Status { get; init; }
    public PpapSubmissionReason Reason { get; init; }
    public string? PartRevision { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? CustomerContactName { get; init; }
    public string? CustomerResponseNotes { get; init; }
    public string? InternalNotes { get; init; }
    public string? PswSignedByName { get; init; }
    public DateTimeOffset? PswSignedAt { get; init; }
    public int CompletedElements { get; init; }
    public int RequiredElements { get; init; }
    public IReadOnlyList<PpapElementResponseModel> Elements { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
}
