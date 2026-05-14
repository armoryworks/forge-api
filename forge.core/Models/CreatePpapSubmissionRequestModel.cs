
namespace Forge.Core.Models;

public record CreatePpapSubmissionRequestModel
{
    public int PartId { get; init; }
    public int CustomerId { get; init; }
    public int PpapLevel { get; init; } = 3;
    public PpapSubmissionReason Reason { get; init; }
    public string? PartRevision { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? CustomerContactName { get; init; }
    public string? InternalNotes { get; init; }
}
