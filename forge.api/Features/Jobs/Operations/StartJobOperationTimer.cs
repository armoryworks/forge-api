using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Jobs.Operations;

public record StartJobOperationTimerCommand(int JobId, int OperationId, StartJobOperationTimerRequestModel Data)
    : IRequest<JobOperationTimerResponseModel>;

public class StartJobOperationTimerValidator : AbstractValidator<StartJobOperationTimerCommand>
{
    public StartJobOperationTimerValidator()
    {
        RuleFor(x => x.Data.EntryType).IsInEnum();
    }
}

public class StartJobOperationTimerHandler(
    IJobOperationService operations,
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHttpContextAccessor httpContext,
    IHubContext<TimerHub> timerHub,
    IHubContext<BoardHub> boardHub,
    IMediator mediator,
    IClock clock) : IRequestHandler<StartJobOperationTimerCommand, JobOperationTimerResponseModel>
{
    private const string OpenTimerIndex = "ux_time_entries_open_job_operation";

    public async Task<JobOperationTimerResponseModel> Handle(StartJobOperationTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!await operations.IsTrackingEnabledAsync(cancellationToken))
            throw new InvalidOperationException(JobOperationRules.TrackingOffMessage);

        var (job, operation) = await operations.FindRoutingStepAsync(request.JobId, request.OperationId, cancellationToken);
        if (job.CompletedDate.HasValue)
            throw new InvalidOperationException($"Job {job.JobNumber} is already complete.");
        if (job.IsArchived || job.Disposition.HasValue)
            throw new InvalidOperationException("This work order is closed and can't take time.");

        var row = await operations.EnsureRowAsync(job, operation, cancellationToken);
        if (JobOperationRules.IsClosed(row.Status))
            throw new InvalidOperationException(JobOperationRules.ReopenFirstMessage);

        var running = await FindOpenTimerAsync(userId, row.Id, cancellationToken);
        if (running is not null)
            return await RespondAsync(running.Id, alreadyRunning: true, request, cancellationToken);

        var now = clock.UtcNow;
        var started = row.Status == JobOperationStatus.NotStarted;
        if (started)
        {
            var jobQuantity = await operations.GetJobQuantityAsync(job, cancellationToken);
            var before = JobOperationRules.Describe(row, jobQuantity);
            row.Status = JobOperationStatus.InProgress;
            row.StartedAt ??= now;
            db.JobActivityLogs.Add(new JobActivityLog
            {
                JobId = job.Id,
                UserId = userId,
                Action = ActivityAction.OperationStarted,
                FieldName = "OperationStatus",
                OldValue = before,
                NewValue = JobOperationRules.Describe(row, jobQuantity),
                Description = $"Started operation {operation.StepNumber} {operation.Title}.",
                CreatedAt = now,
                OperationId = operation.Id,
                WorkCenterId = operation.WorkCenterId,
            });
        }

        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = job.Id,
            OperationId = operation.Id,
            JobOperationId = row.Id,
            EntryType = request.Data.EntryType,
            Category = "Production",
            Date = DateOnly.FromDateTime(ClockStateRules.LocalToday(
                await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken), now)),
            DurationMinutes = 0,
            TimerStart = now,
            IsManual = false,
        };

        try
        {
            await repo.AddTimeEntryAsync(entry, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OpenTimerIndex })
        {
            db.ChangeTracker.Clear();
            var winner = await FindOpenTimerAsync(userId, row.Id, cancellationToken)
                ?? throw new InvalidOperationException(JobOperationRules.StaleMessage);
            return await RespondAsync(winner.Id, alreadyRunning: true, request, cancellationToken);
        }

        db.LogActivityAt("timer-started",
            $"Started timer on operation {operation.StepNumber} {operation.Title}",
            ("TimeEntry", entry.Id));
        await db.SaveChangesAsync(cancellationToken);

        var response = await RespondAsync(entry.Id, alreadyRunning: false, request, cancellationToken);

        await timerHub.Clients.Group($"user:{userId}")
            .SendAsync("timerStarted", new TimerStartedEvent(userId, response.Entry), cancellationToken);
        if (started)
            await JobOperationRules.BroadcastJobUpdatedAsync(boardHub, mediator, job, cancellationToken);

        return response;
    }

    private Task<TimeEntry?> FindOpenTimerAsync(int userId, int jobOperationId, CancellationToken cancellationToken)
        => db.TimeEntries
            .Where(t => t.UserId == userId && t.JobOperationId == jobOperationId
                && t.TimerStart != null && t.TimerStop == null)
            .OrderByDescending(t => t.TimerStart)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<JobOperationTimerResponseModel> RespondAsync(
        int timeEntryId, bool alreadyRunning, StartJobOperationTimerCommand request, CancellationToken cancellationToken)
    {
        var entry = (await repo.GetTimeEntryByIdAsync(timeEntryId, cancellationToken))!;
        var built = await operations.BuildAsync(request.JobId, cancellationToken);
        var row = built.Operations.First(o => o.OperationId == request.OperationId);
        return new JobOperationTimerResponseModel(entry, alreadyRunning, row);
    }
}
