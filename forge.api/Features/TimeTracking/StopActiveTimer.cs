using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.TimeTracking;

public record StopActiveTimerCommand(int UserId, DateTimeOffset StopAt, string? Notes = null, string? Reason = null)
    : IRequest<StoppedTimerResponseModel?>;

public class StopActiveTimerHandler(
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHubContext<TimerHub> timerHub,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    UserManager<ApplicationUser> userManager,
    IJobRepository jobRepository,
    ICustomerRepository customerRepository,
    ILogger<StopActiveTimerHandler> logger) : IRequestHandler<StopActiveTimerCommand, StoppedTimerResponseModel?>
{
    public async Task<StoppedTimerResponseModel?> Handle(StopActiveTimerCommand request, CancellationToken cancellationToken)
    {
        var active = await repo.GetActiveTimerAsync(request.UserId, cancellationToken);
        if (active is null)
            return null;

        active.TimerStop = request.StopAt;
        active.DurationMinutes = (int)Math.Round(
            (request.StopAt - active.TimerStart!.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        if (!string.IsNullOrWhiteSpace(request.Notes))
            active.Notes = request.Notes.Trim();

        var description = $"Stopped timer at {active.DurationMinutes} min";
        if (!string.IsNullOrWhiteSpace(request.Reason))
            description += $" ({request.Reason})";
        db.LogActivityAt("timer-stopped", description, ("TimeEntry", active.Id));

        await repo.SaveChangesAsync(cancellationToken);

        var result = (await repo.GetTimeEntryByIdAsync(active.Id, cancellationToken))!;

        await timerHub.Clients.Group($"user:{request.UserId}")
            .SendAsync("timerStopped", new TimerStoppedEvent(request.UserId, result), cancellationToken);

        if (active.DurationMinutes != 0)
            await EnqueueTimeActivityAsync(active, request.UserId, cancellationToken);

        return new StoppedTimerResponseModel(active.Id, result.JobId, result.JobNumber);
    }

    private async Task EnqueueTimeActivityAsync(TimeEntry active, int userId, CancellationToken cancellationToken)
    {
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
            if (accountingService is null)
                return;

            var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
            if (!syncStatus.Connected)
                return;

            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user?.AccountingEmployeeId is null)
                return;

            string? customerExternalId = null;
            if (active.JobId.HasValue)
            {
                var job = await jobRepository.FindAsync(active.JobId.Value, cancellationToken);
                if (job?.CustomerId is not null)
                {
                    var customer = await customerRepository.FindAsync(job.CustomerId.Value, cancellationToken);
                    customerExternalId = customer?.ExternalId;
                }
            }

            var activity = new AccountingTimeActivity(
                EmployeeExternalId: user.AccountingEmployeeId,
                CustomerExternalId: customerExternalId,
                Hours: active.DurationMinutes / 60m,
                Date: active.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Description: active.Notes ?? $"Time entry for {active.Category ?? "general"}",
                ServiceItemExternalId: null);

            var payload = JsonSerializer.Serialize(activity);
            await syncQueue.EnqueueAsync("TimeEntry", active.Id, "CreateTimeActivity", payload, cancellationToken);
            logger.LogInformation("Enqueued CreateTimeActivity sync for TimeEntry {TimeEntryId}", active.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue time activity sync for TimeEntry {TimeEntryId} — continuing", active.Id);
        }
    }
}
