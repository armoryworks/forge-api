using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Bulk;

public record BulkMoveJobStageCommand(List<int> JobIds, int StageId) : IRequest<BulkOperationResponseModel>;

public class BulkMoveJobStageHandler(
    IJobRepository jobRepo,
    ITrackTypeRepository trackRepo,
    IActivityLogRepository actRepo,
    IWorkCenterContext workCenterContext,
    IHubContext<BoardHub> boardHub) : IRequestHandler<BulkMoveJobStageCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkMoveJobStageCommand request, CancellationToken ct)
    {
        var targetStage = await trackRepo.FindStageAsync(request.StageId, ct)
            ?? throw new KeyNotFoundException($"Stage with ID {request.StageId} not found.");

        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;

        var maxPosition = await jobRepo.GetMaxBoardPositionAsync(request.StageId, ct);

        foreach (var job in jobs)
        {
            if (job.TrackTypeId != targetStage.TrackTypeId)
            {
                errors.Add(new BulkOperationError(job.Id, $"Job {job.JobNumber} belongs to a different track type."));
                continue;
            }

            var previousStageName = job.CurrentStage.Name;
            var previousStageId = job.CurrentStageId;

            job.CurrentStageId = request.StageId;
            job.BoardPosition = ++maxPosition;

            // Bulk moves are typically driven by Mike from his desk, not from a
            // kiosk — but if a worker happens to be timing on a job in this
            // batch, we still capture their work center for the audit row.
            var (workCenterId, operationId) = await workCenterContext.ResolveForJobAsync(
                job.Id, null, ct);

            await actRepo.AddAsync(new JobActivityLog
            {
                JobId = job.Id,
                Action = ActivityAction.StageMoved,
                FieldName = "CurrentStageId",
                OldValue = previousStageName,
                NewValue = targetStage.Name,
                Description = $"Moved from {previousStageName} to {targetStage.Name} (bulk).",
                WorkCenterId = workCenterId,
                OperationId = operationId,
            }, ct);

            successCount++;

            await boardHub.Clients.Group($"board:{job.TrackTypeId}")
                .SendAsync("jobMoved", new BoardJobMovedEvent(
                    job.Id, previousStageId, request.StageId,
                    targetStage.Name, job.BoardPosition), ct);
        }

        // Add errors for missing jobs
        var foundIds = jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in request.JobIds.Where(id => !foundIds.Contains(id)))
            errors.Add(new BulkOperationError(id, $"Job with ID {id} not found."));

        await jobRepo.SaveChangesAsync(ct);

        return new BulkOperationResponseModel(successCount, errors.Count, errors);
    }
}
