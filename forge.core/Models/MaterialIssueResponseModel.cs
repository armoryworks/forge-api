using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MaterialIssueResponseModel
{
    public int Id { get; init; }
    public int JobId { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public int? OperationId { get; init; }
    public string? OperationName { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TotalCost { get; init; }
    public string IssuedByName { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public string? LotNumber { get; init; }
    public MaterialIssueType IssueType { get; init; }
    public string? Notes { get; init; }
}
