using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class PickWave : BaseAuditableEntity
{
    public string WaveNumber { get; set; } = string.Empty;
    public PickWaveStatus Status { get; set; } = PickWaveStatus.Draft;
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? AssignedToId { get; set; }
    public PickWaveStrategy Strategy { get; set; } = PickWaveStrategy.Zone;
    public int TotalLines { get; set; }
    public int PickedLines { get; set; }
    public string? Notes { get; set; }

    public ICollection<PickLine> Lines { get; set; } = [];
}
