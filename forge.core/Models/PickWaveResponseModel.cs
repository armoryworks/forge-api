
namespace Forge.Core.Models;

public record PickWaveResponseModel
{
    public int Id { get; init; }
    public string WaveNumber { get; init; } = string.Empty;
    public PickWaveStatus Status { get; init; }
    public PickWaveStrategy Strategy { get; init; }
    public int? AssignedToId { get; init; }
    public string? AssignedToName { get; init; }
    public int TotalLines { get; init; }
    public int PickedLines { get; init; }
    public DateTimeOffset? ReleasedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public List<PickLineResponseModel> Lines { get; init; } = [];
}
