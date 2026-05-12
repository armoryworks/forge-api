namespace Forge.Core.Models;

public record UpdatePpapSubmissionRequestModel
{
    public int? PpapLevel { get; init; }
    public string? PartRevision { get; init; }
    public DateTimeOffset? DueDate { get; init; }
    public string? CustomerContactName { get; init; }
    public string? CustomerResponseNotes { get; init; }
    public string? InternalNotes { get; init; }
}
