using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Bulk;

public record BulkMoveJobStageCommand(List<int> JobIds, int StageId) : IRequest<BulkOperationResponseModel>;

public class BulkMoveJobStageHandler(
    IJobRepository jobRepo,
    ITrackTypeRepository trackRepo,
    IActivityLogRepository actRepo,
    IWorkCenterContext workCenterContext,
    AppDbContext db,
    IHubContext<BoardHub> boardHub) : IRequestHandler<BulkMoveJobStageCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkMoveJobStageCommand request, CancellationToken ct)
    {
        var targetStage = await trackRepo.FindStageAsync(request.StageId, ct)
            ?? throw new KeyNotFoundException($"Stage with ID {request.StageId} not found.");

        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;

        // Guard context, loaded once for the batch (parity with MoveJobStageHandler,
        // per-job — no per-iteration queries).
        var allStages = await trackRepo.GetStagesByTrackTypeAsync(targetStage.TrackTypeId, ct);
        var lastStage = allStages.OrderByDescending(s => s.SortOrder).FirstOrDefault();
        var movingToCompletion = lastStage is not null && lastStage.Id == request.StageId;

        // F-JQ1 quality-gate data: which of these jobs have an open NCR or a
        // failed QC inspection? Only needed when the target is the final stage.
        var jobIdsInBatch = jobs.Select(j => j.Id).ToList();
        var jobIdsWithOpenNcr = new HashSet<int>();
        var jobIdsWithFailedInspection = new HashSet<int>();
        if (movingToCompletion && jobIdsInBatch.Count > 0)
        {
            jobIdsWithOpenNcr = (await db.NonConformances
                .Where(n => n.JobId != null && jobIdsInBatch.Contains(n.JobId.Value) && n.Status == NcrStatus.Open)
                .Select(n => n.JobId!.Value)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
            jobIdsWithFailedInspection = (await db.QcInspections
                .Where(i => i.JobId != null && jobIdsInBatch.Contains(i.JobId.Value) && i.Status == "Failed")
                .Select(i => i.JobId!.Value)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
        }

        var maxPosition = await jobRepo.GetMaxBoardPositionAsync(request.StageId, ct);

        foreach (var job in jobs)
        {
            var validationError = ValidateMove(
                job, targetStage, allStages, movingToCompletion,
                jobIdsWithOpenNcr, jobIdsWithFailedInspection);
            if (validationError is not null)
            {
                errors.Add(new BulkOperationError(job.Id, validationError));
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

    /// <summary>
    /// Per-job move validation — full parity with MoveJobStageHandler: track
    /// type, irreversible backward guard, mandatory-skip guard, and the F-JQ1
    /// NCR/QC gate on final-stage entry. Returns the error message for the
    /// per-item failure list, or null when the move is allowed.
    /// </summary>
    private static string? ValidateMove(
        Job job,
        JobStage targetStage,
        List<JobStage> allStages,
        bool movingToCompletion,
        HashSet<int> jobIdsWithOpenNcr,
        HashSet<int> jobIdsWithFailedInspection)
    {
        if (job.TrackTypeId != targetStage.TrackTypeId)
            return $"Job {job.JobNumber} belongs to a different track type.";

        var currentStage = job.CurrentStage;

        // Backward move enforcement: block moves away from irreversible stages
        if (currentStage.IsIrreversible && targetStage.SortOrder < currentStage.SortOrder)
        {
            return $"Job {job.JobNumber}: cannot move backward from irreversible stage " +
                $"'{currentStage.Name}'. Documents created at this stage cannot be voided.";
        }

        // Workflow sequencing: a forward move must not skip an active mandatory stage.
        if (targetStage.SortOrder > currentStage.SortOrder)
        {
            var skippedMandatory = allStages
                .Where(s => s.IsMandatory
                    && s.SortOrder > currentStage.SortOrder
                    && s.SortOrder < targetStage.SortOrder)
                .OrderBy(s => s.SortOrder)
                .Select(s => $"'{s.Name}'")
                .ToList();

            if (skippedMandatory.Count > 0)
            {
                return $"Job {job.JobNumber}: cannot move to '{targetStage.Name}' — this would skip " +
                    $"the mandatory stage{(skippedMandatory.Count > 1 ? "s" : "")} " +
                    $"{string.Join(", ", skippedMandatory)}. " +
                    "Move the job through each mandatory stage in order.";
            }
        }

        // F-JQ1: no advancing into completion with an open NCR or failed QC inspection.
        if (movingToCompletion
            && (jobIdsWithOpenNcr.Contains(job.Id) || jobIdsWithFailedInspection.Contains(job.Id)))
        {
            return $"Job {job.JobNumber}: cannot complete this job while it has an open " +
                "non-conformance (NCR) or a failed QC inspection. Resolve the open quality " +
                "issue before advancing to the final stage.";
        }

        return null;
    }
}
