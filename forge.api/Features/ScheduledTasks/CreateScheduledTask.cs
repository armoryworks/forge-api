using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public record CreateScheduledTaskCommand(
    string Name,
    string? Description,
    int TrackTypeId,
    int? InternalProjectTypeId,
    int? AssigneeId,
    string CronExpression) : IRequest<ScheduledTaskResponseModel>;

public class CreateScheduledTaskValidator : AbstractValidator<CreateScheduledTaskCommand>
{
    public CreateScheduledTaskValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TrackTypeId).GreaterThan(0);
        RuleFor(x => x.CronExpression).NotEmpty().MaximumLength(100);
    }
}

public class CreateScheduledTaskHandler(AppDbContext db) : IRequestHandler<CreateScheduledTaskCommand, ScheduledTaskResponseModel>
{
    public async Task<ScheduledTaskResponseModel> Handle(CreateScheduledTaskCommand request, CancellationToken ct)
    {
        var trackType = await db.TrackTypes.FirstOrDefaultAsync(t => t.Id == request.TrackTypeId, ct)
            ?? throw new KeyNotFoundException($"Track type {request.TrackTypeId} not found.");

        var task = new ScheduledTask
        {
            Name = request.Name,
            Description = request.Description,
            TrackTypeId = request.TrackTypeId,
            InternalProjectTypeId = request.InternalProjectTypeId,
            AssigneeId = request.AssigneeId,
            CronExpression = request.CronExpression,
            NextRunAt = DateTimeOffset.UtcNow, // Will be calculated by the job runner
        };

        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync(ct);

        return new ScheduledTaskResponseModel(
            task.Id, task.Name, task.Description, task.TrackTypeId, trackType.Name,
            task.InternalProjectTypeId, task.AssigneeId, task.CronExpression,
            task.IsActive, task.LastRunAt, task.NextRunAt, task.CreatedAt);
    }
}
