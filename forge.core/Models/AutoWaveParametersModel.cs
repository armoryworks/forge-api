
namespace Forge.Core.Models;

public record AutoWaveParametersModel
{
    public PickWaveStrategy Strategy { get; init; } = PickWaveStrategy.Zone;
    public DateTimeOffset? ShipByDateFrom { get; init; }
    public DateTimeOffset? ShipByDateTo { get; init; }
    public int MaxLinesPerWave { get; init; } = 50;
    public int? AssignedToId { get; init; }
}
