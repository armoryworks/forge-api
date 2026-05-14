using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Bulk;

public record BulkSetPriorityCommand(List<int> JobIds, string Priority) : IRequest<BulkOperationResponseModel>;

public class BulkSetPriorityHandler(
    IJobRepository jobRepo,
    IActivityLogRepository actRepo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<BulkSetPriorityCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkSetPriorityCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<JobPriority>(request.Priority, out var priority))
            throw new InvalidOperationException($"Invalid priority: {request.Priority}");

        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;

        foreach (var job in jobs)
        {
            var oldPriority = job.Priority.ToString();
            job.Priority = priority;

            await actRepo.AddAsync(new JobActivityLog
            {
                JobId = job.Id,
                Action = ActivityAction.FieldChanged,
                FieldName = "Priority",
                OldValue = oldPriority,
                NewValue = request.Priority,
                Description = $"Priority changed from {oldPriority} to {request.Priority} (bulk).",
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
                .SendAsync("boardUpdated", new { reason = "bulk-priority" }, ct);
        }

        return new BulkOperationResponseModel(successCount, errors.Count, errors);
    }
}
