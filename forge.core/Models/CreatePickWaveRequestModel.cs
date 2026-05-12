using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreatePickWaveRequestModel
{
    public List<int> ShipmentLineIds { get; init; } = [];
    public PickWaveStrategy Strategy { get; init; } = PickWaveStrategy.Zone;
    public int? AssignedToId { get; init; }
    public string? Notes { get; init; }
}
