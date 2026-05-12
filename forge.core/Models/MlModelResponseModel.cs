using Forge.Core.Enums;

namespace Forge.Core.Models;

public record MlModelResponseModel
{
    public string ModelId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ModelType { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public MlModelStatus Status { get; init; }
    public DateTimeOffset TrainedAt { get; init; }
    public int TrainingSampleCount { get; init; }
    public decimal? Accuracy { get; init; }
    public decimal? Precision { get; init; }
    public decimal? Recall { get; init; }
    public decimal? F1Score { get; init; }
    public string PredictionType { get; init; } = string.Empty;
    public string? WorkCenterName { get; init; }
}
