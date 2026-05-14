
namespace Forge.Core.Models;

public record UpdateFmeaRequestModel
{
    public string? Name { get; init; }
    public FmeaStatus? Status { get; init; }
    public string? PreparedBy { get; init; }
    public string? Responsibility { get; init; }
    public DateOnly? RevisionDate { get; init; }
    public string? Notes { get; init; }
}
