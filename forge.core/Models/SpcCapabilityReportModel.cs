namespace Forge.Core.Models;

public record SpcCapabilityReportModel
{
    public int CharacteristicId { get; init; }
    public string CharacteristicName { get; init; } = string.Empty;
    public decimal Usl { get; init; }
    public decimal Lsl { get; init; }
    public decimal Nominal { get; init; }
    public decimal Cp { get; init; }
    public decimal Cpk { get; init; }
    public decimal Pp { get; init; }
    public decimal Ppk { get; init; }
    public decimal Mean { get; init; }
    public decimal Sigma { get; init; }
    public int SampleCount { get; init; }
    public IReadOnlyList<HistogramBucket> HistogramBuckets { get; init; } = [];
    public IReadOnlyList<NormalCurvePoint> NormalCurve { get; init; } = [];
}

public record HistogramBucket(decimal From, decimal To, int Count);

public record NormalCurvePoint(decimal X, decimal Y);
