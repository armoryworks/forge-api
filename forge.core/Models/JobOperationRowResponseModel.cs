using Forge.Core.Enums;

namespace Forge.Core.Models;

public record JobOperationRowResponseModel
{
    public int? OperationId { get; init; }
    public int? JobOperationId { get; init; }
    public uint? Version { get; init; }
    public int StepNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? WorkCenterName { get; init; }
    public bool IsRoutingStep { get; init; }
    public JobOperationStatus Status { get; init; }
    public decimal CompletedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? CompletedByName { get; init; }
    public decimal EstimatedSetupMinutes { get; init; }
    public decimal EstimatedRunMinutesEach { get; init; }
    public decimal EstimatedRunMinutesLot { get; init; }
    public decimal EstimatedTotalMinutes { get; init; }
    public decimal ActualSetupMinutes { get; init; }
    public decimal ActualRunMinutes { get; init; }
    public decimal ActualOtherMinutes { get; init; }
    public decimal ActualTotalMinutes { get; init; }
    public decimal? ActualRunMinutesEach { get; init; }
    public decimal RemainingMinutes { get; init; }
    public decimal? HistoryRunMinutesEach { get; init; }
    public int HistoryJobCount { get; init; }
    public List<JobOperationOpenTimerResponseModel> OpenTimers { get; init; } = [];
}
