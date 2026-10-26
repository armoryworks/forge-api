using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
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
        RuleFor(x => x.CronExpression).NotEmpty().MaximumLength(100)
            .Must(ReportScheduleCron.IsValid).WithMessage("Cron expression is not valid.").WithErrorCode("cron-invalid");
    }
}

public class CreateScheduledTaskHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateScheduledTaskCommand, ScheduledTaskResponseModel>
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
            NextRunAt = await ScheduledTaskCron.NextRunAfterAsync(db, request.CronExpression, clock.UtcNow, ct),
        };

        db.ScheduledTasks.Add(task);
        await db.SaveChangesAsync(ct);

        return new ScheduledTaskResponseModel(
            task.Id, task.Name, task.Description, task.TrackTypeId, trackType.Name,
            task.InternalProjectTypeId, task.AssigneeId, task.CronExpression,
            task.IsActive, task.LastRunAt, task.NextRunAt, task.CreatedAt);
    }
}
