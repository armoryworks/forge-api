using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Operations;

public record UpdateJobOperationProgressCommand(int JobId, int OperationId, UpdateJobOperationProgressRequestModel Data)
    : IRequest<JobOperationProgressResponseModel>;

public class UpdateJobOperationProgressValidator : AbstractValidator<UpdateJobOperationProgressCommand>
{
    public UpdateJobOperationProgressValidator()
    {
        RuleFor(x => x.Data)
            .Must(d => d.CompletedQuantity.HasValue || d.ScrapQuantity.HasValue || d.Status.HasValue)
            .WithMessage("Send a completed quantity, a scrap quantity or a status.");
        RuleFor(x => x.Data.CompletedQuantity).GreaterThanOrEqualTo(0m).When(x => x.Data.CompletedQuantity.HasValue);
        RuleFor(x => x.Data.ScrapQuantity).GreaterThanOrEqualTo(0m).When(x => x.Data.ScrapQuantity.HasValue);
        RuleFor(x => x.Data.Status).IsInEnum().When(x => x.Data.Status.HasValue);
    }
}

public class UpdateJobOperationProgressHandler(
    IJobOperationService operations,
    ITimerStopService timerStop,
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IHubContext<BoardHub> boardHub,
    IMediator mediator,
    IClock clock) : IRequestHandler<UpdateJobOperationProgressCommand, JobOperationProgressResponseModel>
{
    public async Task<JobOperationProgressResponseModel> Handle(UpdateJobOperationProgressCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var data = request.Data;

        if (!await operations.IsTrackingEnabledAsync(cancellationToken))
            throw new InvalidOperationException(JobOperationRules.TrackingOffMessage);

        var (job, operation) = await operations.FindRoutingStepAsync(request.JobId, request.OperationId, cancellationToken);
        var row = await operations.EnsureRowAsync(job, operation, cancellationToken);
        if (data.ExpectedVersion is uint expected && expected != row.Version)
            throw new InvalidOperationException(JobOperationRules.StaleMessage);

        var jobQuantity = await operations.GetJobQuantityAsync(job, cancellationToken);
        var before = JobOperationRules.Describe(row, jobQuantity);
        var wasClosed = JobOperationRules.IsClosed(row.Status);
        var now = clock.UtcNow;

        var openTimers = await db.TimeEntries
            .Where(t => t.JobOperationId == row.Id && t.TimerStart != null && t.TimerStop == null)
            .ToListAsync(cancellationToken);

        var scrap = data.ScrapQuantity ?? row.ScrapQuantity;
        var completed = data.CompletedQuantity ?? row.CompletedQuantity;
        var closeTimers = false;
        ActivityAction action;

        switch (data.Status)
        {
            case JobOperationStatus.Complete:
                completed = data.CompletedQuantity ?? Math.Max(row.CompletedQuantity, jobQuantity - scrap);
                row.Status = JobOperationStatus.Complete;
                row.StartedAt ??= now;
                row.CompletedAt = now;
                row.CompletedById = userId;
                closeTimers = true;
                action = ActivityAction.OperationCompleted;
                break;
            case JobOperationStatus.Skipped:
                row.Status = JobOperationStatus.Skipped;
                row.CompletedAt = now;
                row.CompletedById = userId;
                closeTimers = true;
                action = ActivityAction.OperationSkipped;
                break;
            case JobOperationStatus.InProgress:
                if (wasClosed)
                {
                    row.CompletedAt = null;
                    row.CompletedById = null;
                }
                row.Status = JobOperationStatus.InProgress;
                row.StartedAt ??= now;
                action = wasClosed ? ActivityAction.OperationReopened : ActivityAction.OperationProgress;
                break;
            case JobOperationStatus.NotStarted:
                if (openTimers.Count > 0)
                    throw new InvalidOperationException("Stop the running timers on this operation before resetting it.");
                completed = 0m;
                scrap = 0m;
                row.Status = JobOperationStatus.NotStarted;
                row.StartedAt = null;
                row.CompletedAt = null;
                row.CompletedById = null;
                action = ActivityAction.OperationReopened;
                break;
            default:
                if (row.Status == JobOperationStatus.NotStarted)
                {
                    row.Status = JobOperationStatus.InProgress;
                    row.StartedAt ??= now;
                }
                action = ActivityAction.OperationProgress;
                break;
        }

        if (completed + scrap > jobQuantity)
            throw new ValidationException([
                new ValidationFailure(nameof(data.CompletedQuantity),
                    $"Completed ({completed:0.##}) plus scrap ({scrap:0.##}) is more than the job quantity ({jobQuantity:0.##})."),
            ]);

        row.CompletedQuantity = completed;
        row.ScrapQuantity = scrap;

        var closed = new List<TimeEntry>();
        if (closeTimers && openTimers.Count > 0)
        {
            var actorName = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.LastName + ", " + u.FirstName)
                .FirstOrDefaultAsync(cancellationToken) ?? "another user";
            var verb = row.Status == JobOperationStatus.Skipped ? "skipped" : "completed";
            foreach (var timer in openTimers)
            {
                timerStop.Close(timer, now,
                    reason: $"operation {verb}",
                    appendNote: $"stopped: operation {verb} by {actorName}");
                closed.Add(timer);
            }
        }

        var after = JobOperationRules.Describe(row, jobQuantity);
        db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = job.Id,
            UserId = userId,
            Action = action,
            FieldName = "OperationStatus",
            OldValue = before,
            NewValue = after,
            Description = $"Operation {operation.StepNumber} {operation.Title}: {before} → {after}.",
            CreatedAt = now,
            OperationId = operation.Id,
            WorkCenterId = operation.WorkCenterId,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(JobOperationRules.StaleMessage);
        }

        if (closed.Count > 0)
            await timerStop.PublishStoppedAsync(closed, cancellationToken);
        await JobOperationRules.BroadcastJobUpdatedAsync(boardHub, mediator, job, cancellationToken);

        var built = await operations.BuildAsync(job.Id, cancellationToken);
        return new JobOperationProgressResponseModel(
            built.Operations.First(o => o.OperationId == operation.Id),
            built.AllOperationsComplete,
            built.EstimatedRemainingMinutes);
    }
}
