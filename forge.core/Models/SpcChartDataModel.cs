namespace Forge.Core.Models;

public record SpcChartDataModel
{
    public int CharacteristicId { get; init; }
    public string CharacteristicName { get; init; } = string.Empty;
    public decimal Usl { get; init; }
    public decimal Lsl { get; init; }
    public decimal Nominal { get; init; }
    public SpcControlLimitModel? ActiveLimits { get; init; }
    public IReadOnlyList<SpcChartPointModel> Points { get; init; } = [];
}

public record SpcControlLimitModel
{
    public decimal XBarUcl { get; init; }
    public decimal XBarLcl { get; init; }
    public decimal XBarCenterLine { get; init; }
    public decimal RangeUcl { get; init; }
    public decimal RangeLcl { get; init; }
    public decimal RangeCenterLine { get; init; }
    public decimal Cp { get; init; }
    public decimal Cpk { get; init; }
    public decimal Pp { get; init; }
    public decimal Ppk { get; init; }
    public decimal ProcessSigma { get; init; }
    public int SampleCount { get; init; }
    public bool IsActive { get; init; }
}

public record SpcChartPointModel
{
    public int SubgroupNumber { get; init; }
    public DateTimeOffset MeasuredAt { get; init; }
    public decimal Mean { get; init; }
    public decimal Range { get; init; }
    public decimal? StdDev { get; init; }
    public bool IsOoc { get; init; }
    public string? OocRule { get; init; }
}
