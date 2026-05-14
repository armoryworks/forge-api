
namespace Forge.Core.Models;

public record CreatePartAlternateRequestModel
{
    public int AlternatePartId { get; init; }
    public int Priority { get; init; } = 1;
    public AlternateType Type { get; init; } = AlternateType.Substitute;
    public decimal? ConversionFactor { get; init; }
    public bool IsApproved { get; init; }
    public string? Notes { get; init; }
    public bool IsBidirectional { get; init; }
}
