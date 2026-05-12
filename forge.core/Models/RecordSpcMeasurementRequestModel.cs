namespace Forge.Core.Models;

public record RecordSpcMeasurementRequestModel
{
    public int CharacteristicId { get; init; }
    public int? JobId { get; init; }
    public int? ProductionRunId { get; init; }
    public string? LotNumber { get; init; }
    public IReadOnlyList<SpcSubgroupEntry> Subgroups { get; init; } = [];
}

public record SpcSubgroupEntry
{
    public decimal[] Values { get; init; } = [];
    public string? Notes { get; init; }
}
