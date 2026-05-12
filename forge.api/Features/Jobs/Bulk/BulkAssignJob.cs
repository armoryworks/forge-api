using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Bulk;

public record BulkAssignJobCommand(List<int> JobIds, int? AssigneeId) : IRequest<BulkOperationResponseModel>;

public class BulkAssignJobHandler(
    IJobRepository jobRepo,
    IActivityLogRepository actRepo,
    IHubContext<BoardHub> boardHub,
    AppDbContext db) : IRequestHandler<BulkAssignJobCommand, BulkOperationResponseModel>
{
    public async Task<BulkOperationResponseModel> Handle(BulkAssignJobCommand request, CancellationToken ct)
    {
        if (request.AssigneeId.HasValue)
            await AssigneeComplianceCheck.EnsureCanBeAssigned(db, request.AssigneeId.Value, ct);

        var jobs = await jobRepo.FindMultipleAsync(request.JobIds, ct);
        var errors = new List<BulkOperationError>();
        var successCount = 0;

        foreach (var job in jobs)
        {
            var oldAssigneeId = job.AssigneeId;
            job.AssigneeId = request.AssigneeId;

            var action = request.AssigneeId.HasValue ? ActivityAction.Assigned : ActivityAction.Unassigned;
            await actRepo.AddAsync(new JobActivityLog
            {
                JobId = job.Id,
                Action = action,
                FieldName = "AssigneeId",
                OldValue = oldAssigneeId?.ToString(),
                NewValue = request.AssigneeId?.ToString(),
                Description = request.AssigneeId.HasValue
                    ? $"Assigned (bulk)."
                    : "Unassigned (bulk).",
            }, ct);

            successCount++;
        }

        var foundIds = jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in request.JobIds.Where(id => !foundIds.Contains(id)))
            errors.Add(new BulkOperationError(id, $"Job with ID {id} not found."));

        await jobRepo.SaveChangesAsync(ct);

        // Broadcast board refresh for affected track types
        var trackTypeIds = jobs.Select(j => j.TrackTypeId).Distinct();
        foreach (var trackTypeId in trackTypeIds)
        {
            await boardHub.Clients.Group($"board:{trackTypeId}")
                .SendAsync("boardUpdated", new { reason = "bulk-assign" }, ct);
        }

        return new BulkOperationResponseModel(successCount, errors.Count, errors);
    }
}
