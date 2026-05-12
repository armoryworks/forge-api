using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Bulk;

public record BulkArchiveJobsCommand(List<int> JobIds) : IRequest<BulkOperationResponseModel>;

public class BulkArchiveJobsHandler(
    IJobRepository jobRepo,
    IActivityLogRepository actRepo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<BulkArchiveJobsCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkArchiveJobsCommand request, CancellationToken ct)
    {
        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;

        foreach (var job in jobs)
        {
            if (job.IsArchived)
            {
                errors.Add(new BulkOperationError(job.Id, $"Job {job.JobNumber} is already archived."));
                continue;
            }

            job.IsArchived = true;

            await actRepo.AddAsync(new JobActivityLog
            {
                JobId = job.Id,
                Action = ActivityAction.Archived,
                Description = "Archived (bulk).",
            }, ct);

            successCount++;
        }

        var foundIds = jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in request.JobIds.Where(id => !foundIds.Contains(id)))
            errors.Add(new BulkOperationError(id, $"Job with ID {id} not found."));

        await jobRepo.SaveChangesAsync(ct);

        var trackTypeIds = jobs.Select(j => j.TrackTypeId).Distinct();
        foreach (var trackTypeId in trackTypeIds)
        {
            await boardHub.Clients.Group($"board:{trackTypeId}")
                .SendAsync("boardUpdated", new { reason = "bulk-archive" }, ct);
        }

        return new BulkOperationResponseModel(successCount, errors.Count, errors);
    }
}
