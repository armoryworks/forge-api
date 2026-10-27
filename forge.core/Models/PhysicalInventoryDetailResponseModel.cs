namespace Forge.Core.Models;

public record PhysicalInventoryDetailResponseModel : PhysicalInventoryResponseModel
{
    public IReadOnlyList<PhysicalInventoryTagResponseModel> Tags { get; init; } = [];
}
