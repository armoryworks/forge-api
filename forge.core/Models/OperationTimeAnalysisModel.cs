namespace Forge.Core.Models;

public record OperationTimeAnalysisModel
{
    public int OperationId { get; init; }
    public string OperationName { get; init; } = string.Empty;
    public int OperationSequence { get; init; }
    public decimal EstimatedSetupMinutes { get; init; }
    public decimal EstimatedRunMinutes { get; init; }
    public decimal ActualSetupMinutes { get; init; }
    public decimal ActualRunMinutes { get; init; }
    public decimal ActualTotalMinutes { get; init; }
    public decimal SetupVarianceMinutes { get; init; }
    public decimal RunVarianceMinutes { get; init; }
    public decimal EfficiencyPercent { get; init; }
    public int EntryCount { get; init; }
}
