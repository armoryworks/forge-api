
namespace Forge.Core.Models;

public record UpdateNcrRequestModel
{
    public NcrType? Type { get; init; }
    public string? Description { get; init; }
    public decimal? AffectedQuantity { get; init; }
    public decimal? DefectiveQuantity { get; init; }
    public string? ContainmentActions { get; init; }
    public decimal? MaterialCost { get; init; }
    public decimal? LaborCost { get; init; }
    public NcrStatus? Status { get; init; }
}
