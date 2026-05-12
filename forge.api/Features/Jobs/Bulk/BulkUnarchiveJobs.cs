using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Bulk;

/// <summary>
/// Inverse of <see cref="BulkArchiveJobsCommand"/>: restores previously
/// archived jobs by clearing the IsArchived flag. Admin-only at the
/// controller boundary. Phase 3 / WU-07 / F2.
/// </summary>
public record BulkUnarchiveJobsCommand(List<int> JobIds) : IRequest<BulkOperationResponseModel>;

public class BulkUnarchiveJobsHandler(
    IJobRepository jobRepo,
    IActivityLogRepository actRepo,
    IHubContext<BoardHub> boardHub,
    ISystemAuditWriter auditWriter,
    AppDbContext db) : IRequestHandler<BulkUnarchiveJobsCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkUnarchiveJobsCommand request, CancellationToken ct)
    {
        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;
        var unarchivedJobIds = new List<int>();

        foreach (var job in jobs)
        {
            if (!job.IsArchived)
            {
                errors.Add(new BulkOperationError(job.Id, $"Job {job.JobNumber} is not archived."));
                continue;
            }

            job.IsArchived = false;

            await actRepo.AddAsync(new JobActivityLog
            {
                JobId = job.Id,
                Action = ActivityAction.Restored,
                Description = "Unarchived (bulk).",
            }, ct);

            unarchivedJobIds.Add(job.Id);
            successCount++;
        }

        var foundIds = jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in request.JobIds.Where(id => !foundIds.Contains(id)))
            errors.Add(new BulkOperationError(id, $"Job with ID {id} not found."));

        await jobRepo.SaveChangesAsync(ct);

        // Emit explicit JobUnarchived audit rows (one per restored job) so the
        // operational gap surfaced by Phase 1C — archive with no recovery path —
        // is now traceable in audit_log_entries with a high-signal action name.
        // The implicit JobUpdated row from the IsArchived field-change is still
        // captured by AppDbContext.CaptureAuditEntries; this is the named
        // companion event the audit subscriber from WU-03 looks for.
        var actorId = db.CurrentUserId ?? 0;
        foreach (var jobId in unarchivedJobIds)
        {
            await auditWriter.WriteAsync(
                "JobUnarchived",
                actorId,
                entityType: "Job",
                entityId: jobId,
                ct: ct);
        }

        var trackTypeIds = jobs.Where(j => unarchivedJobIds.Contains(j.Id))
            .Select(j => j.TrackTypeId).Distinct();
        foreach (var trackTypeId in trackTypeIds)
        {
            await boardHub.Clients.Group($"board:{trackTypeId}")
                .SendAsync("boardUpdated", new { reason = "bulk-unarchive" }, ct);
        }

        return new BulkOperationResponseModel(successCount, errors.Count, errors);
    }
}
