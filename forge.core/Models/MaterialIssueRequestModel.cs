
namespace Forge.Core.Models;

public record MaterialIssueRequestModel
{
    public int PartId { get; init; }
    public int? OperationId { get; init; }
    public decimal Quantity { get; init; }
    public int? BinContentId { get; init; }
    public int? StorageLocationId { get; init; }
    public string? LotNumber { get; init; }
    public MaterialIssueType IssueType { get; init; } = MaterialIssueType.Issue;
    public string? Notes { get; init; }
}
