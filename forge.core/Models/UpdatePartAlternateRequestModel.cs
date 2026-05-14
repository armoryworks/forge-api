
namespace Forge.Core.Models;

public record UpdatePartAlternateRequestModel
{
    public int? Priority { get; init; }
    public AlternateType? Type { get; init; }
    public decimal? ConversionFactor { get; init; }
    public bool? IsApproved { get; init; }
    public string? Notes { get; init; }
    public bool? IsBidirectional { get; init; }
}
