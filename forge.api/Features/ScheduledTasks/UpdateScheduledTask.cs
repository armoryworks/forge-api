using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Reports;
using Forge.Core.Interfaces;
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

public class UpdateScheduledTaskValidator : AbstractValidator<UpdateScheduledTaskCommand>
{
    public UpdateScheduledTaskValidator()
    {
        RuleFor(x => x.CronExpression!).MaximumLength(100)
            .Must(ReportScheduleCron.IsValid).WithMessage("Cron expression is not valid.").WithErrorCode("cron-invalid")
            .When(x => x.CronExpression is not null);
    }
}

public class UpdateScheduledTaskHandler(AppDbContext db, IClock clock) : IRequestHandler<UpdateScheduledTaskCommand, ScheduledTaskResponseModel>
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
        var reschedule = (request.CronExpression != null && request.CronExpression != task.CronExpression)
            || (request.IsActive == true && !task.IsActive);
        if (request.CronExpression != null) task.CronExpression = request.CronExpression;
        if (request.IsActive.HasValue) task.IsActive = request.IsActive.Value;
        if (reschedule)
            task.NextRunAt = await ScheduledTaskCron.NextRunAfterAsync(db, task.CronExpression, clock.UtcNow, ct);

        await db.SaveChangesAsync(ct);

        return new ScheduledTaskResponseModel(
            task.Id, task.Name, task.Description, task.TrackTypeId, task.TrackType.Name,
            task.InternalProjectTypeId, task.AssigneeId, task.CronExpression,
            task.IsActive, task.LastRunAt, task.NextRunAt, task.CreatedAt);
    }
}
