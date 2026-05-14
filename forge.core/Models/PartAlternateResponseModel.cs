
namespace Forge.Core.Models;

public record PartAlternateResponseModel
{
    public int Id { get; init; }
    public int PartId { get; init; }
    public int AlternatePartId { get; init; }
    public string AlternatePartNumber { get; init; } = string.Empty;
    public string AlternatePartName { get; init; } = string.Empty;
    public int Priority { get; init; }
    public AlternateType Type { get; init; }
    public decimal? ConversionFactor { get; init; }
    public bool IsApproved { get; init; }
    public string? ApprovedByName { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? Notes { get; init; }
    public bool IsBidirectional { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
