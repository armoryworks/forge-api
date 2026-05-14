
namespace Forge.Core.Entities;

public class MlModel : BaseEntity
{
    public string ModelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public MlModelStatus Status { get; set; }
    public DateTimeOffset TrainedAt { get; set; }
    public int TrainingSampleCount { get; set; }
    public decimal? Accuracy { get; set; }
    public decimal? Precision { get; set; }
    public decimal? Recall { get; set; }
    public decimal? F1Score { get; set; }
    public string? HyperparametersJson { get; set; }
    public string? FeatureListJson { get; set; }
    public string? ModelArtifactPath { get; set; }
    public int? WorkCenterId { get; set; }
    public string PredictionType { get; set; } = string.Empty;

    // Navigation
    public WorkCenter? WorkCenter { get; set; }
}
