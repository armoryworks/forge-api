namespace Forge.Core.Models;

public record MlModelPerformanceResponseModel
{
    public string ModelId { get; init; } = string.Empty;
    public decimal Accuracy { get; init; }
    public decimal Precision { get; init; }
    public decimal Recall { get; init; }
    public decimal F1Score { get; init; }
    public int TotalPredictions { get; init; }
    public int TruePredictions { get; init; }
    public int FalsePredictions { get; init; }
    public decimal AverageLeadTimeHours { get; init; }
    public IReadOnlyList<PredictionAccuracyTrendPoint> AccuracyTrend { get; init; } = [];
}

public record PredictionAccuracyTrendPoint
{
    public DateOnly Period { get; init; }
    public decimal Accuracy { get; init; }
    public int Predictions { get; init; }
}
