using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record UpdateScheduledTaskCommand(
    int Id,
    string? Name,
    string? Description,
    int? TrackTypeId,
    int? InternalProjectTypeId,
    int? AssigneeId,
    string? CronExpression,
    bool? IsActive) : IRequest<ScheduledTaskResponseModel>;

public class UpdateScheduledTaskHandler(AppDbContext db) : IRequestHandler<UpdateScheduledTaskCommand, ScheduledTaskResponseModel>
{
    public async Task<ScheduledTaskResponseModel> Handle(UpdateScheduledTaskCommand request, CancellationToken ct)
    {
        var task = await db.ScheduledTasks
            .Include(t => t.TrackType)
            .FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Scheduled task {request.Id} not found.");

        if (request.Name != null) task.Name = request.Name;
        if (request.Description != null) task.Description = request.Description;
        if (request.TrackTypeId.HasValue) task.TrackTypeId = request.TrackTypeId.Value;
        if (request.InternalProjectTypeId.HasValue) task.InternalProjectTypeId = request.InternalProjectTypeId;
        if (request.AssigneeId.HasValue) task.AssigneeId = request.AssigneeId;
        if (request.CronExpression != null) task.CronExpression = request.CronExpression;
        if (request.IsActive.HasValue) task.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(ct);

        return new ScheduledTaskResponseModel(
            task.Id, task.Name, task.Description, task.TrackTypeId, task.TrackType.Name,
            task.InternalProjectTypeId, task.AssigneeId, task.CronExpression,
            task.IsActive, task.LastRunAt, task.NextRunAt, task.CreatedAt);
    }
}
