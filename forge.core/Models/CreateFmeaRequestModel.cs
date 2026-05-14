
namespace Forge.Core.Models;

public record CreateFmeaRequestModel
{
    public string Name { get; init; } = string.Empty;
    public FmeaType Type { get; init; }
    public int? PartId { get; init; }
    public int? OperationId { get; init; }
    public string? PreparedBy { get; init; }
    public string? Responsibility { get; init; }
    public int? PpapSubmissionId { get; init; }
    public string? Notes { get; init; }
}
