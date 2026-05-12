namespace Forge.Core.Models;

public record FmeaRiskSummaryResponseModel
{
    public int TotalItems { get; init; }
    public int HighRpnItems { get; init; }
    public decimal AverageRpn { get; init; }
    public int MaxRpn { get; init; }
    public IReadOnlyList<RpnDistributionBucket> RpnDistribution { get; init; } = [];
    public IReadOnlyList<RpnHeatmapCell> HeatmapData { get; init; } = [];
}

public record RpnDistributionBucket
{
    public string Range { get; init; } = string.Empty;
    public int Count { get; init; }
}

public record RpnHeatmapCell
{
    public int Severity { get; init; }
    public int Occurrence { get; init; }
    public int Detection { get; init; }
    public int Count { get; init; }
}
