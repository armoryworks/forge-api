using Forge.Core.Models;

namespace Forge.Api.Features.Mobile;

/// <summary>What a scanned code turned out to be. Kind is one of job, part, bin, lot, badge, salesOrder, purchaseOrder, asset, unknown.</summary>
public record ScanResolveResponseModel(
    string Kind,
    int? Id,
    string Code,
    string Label,
    string? Subtitle);

public record JobStatusResponseModel(
    int Id,
    string JobNumber,
    string Title,
    string? CustomerName,
    int StageId,
    string StageName,
    string StageColor,
    DateTimeOffset? DueDate,
    bool IsOverdue,
    int? NextStageId,
    string? NextStageName,
    int? PreviousStageId,
    string? PreviousStageName,
    uint RowVersion,
    List<ActivityResponseModel> RecentActivity);

/// <summary>Result of advancing a job; PreviousStageId is what undo moves back to.</summary>
public record JobAdvanceResponseModel(
    JobStatusResponseModel Status,
    int PreviousStageId,
    string PreviousStageName,
    bool Collapsed);
