using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Services;

public class TimerStopService(
    ITimeTrackingRepository repo,
    AppDbContext db,
    IHubContext<TimerHub> timerHub,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    UserManager<ApplicationUser> userManager,
    IJobRepository jobRepository,
    ICustomerRepository customerRepository,
    ILogger<TimerStopService> logger) : ITimerStopService
{
    public void Close(TimeEntry entry, DateTimeOffset stopAt, string? notes = null, string? reason = null, string? appendNote = null)
    {
        entry.TimerStop = stopAt;
        entry.DurationMinutes = (int)Math.Round(
            (stopAt - entry.TimerStart!.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        if (!string.IsNullOrWhiteSpace(notes))
            entry.Notes = notes.Trim();
        if (!string.IsNullOrWhiteSpace(appendNote))
            entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? appendNote : $"{entry.Notes} ({appendNote})";

        var description = $"Stopped timer at {entry.DurationMinutes} min";
        if (!string.IsNullOrWhiteSpace(reason))
            description += $" ({reason})";
        db.LogActivityAt("timer-stopped", description, ("TimeEntry", entry.Id));
    }

    public async Task<IReadOnlyList<TimeEntryResponseModel>> PublishStoppedAsync(
        IReadOnlyList<TimeEntry> entries, CancellationToken cancellationToken)
    {
        var results = new List<TimeEntryResponseModel>(entries.Count);
        foreach (var entry in entries)
        {
            var result = (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;
            results.Add(result);

            await timerHub.Clients.Group($"user:{entry.UserId}")
                .SendAsync("timerStopped", new TimerStoppedEvent(entry.UserId, result), cancellationToken);

            if (entry.DurationMinutes != 0)
                await EnqueueTimeActivityAsync(entry, cancellationToken);
        }

        return results;
    }

    private async Task EnqueueTimeActivityAsync(TimeEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
            if (accountingService is null)
                return;

            var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
            if (!syncStatus.Connected)
                return;

            var user = await userManager.FindByIdAsync(entry.UserId.ToString());
            if (user?.AccountingEmployeeId is null)
                return;

            string? customerExternalId = null;
            if (entry.JobId.HasValue)
            {
                var job = await jobRepository.FindAsync(entry.JobId.Value, cancellationToken);
                if (job?.CustomerId is not null)
                {
                    var customer = await customerRepository.FindAsync(job.CustomerId.Value, cancellationToken);
                    customerExternalId = customer?.ExternalId;
                }
            }

            var activity = new AccountingTimeActivity(
                EmployeeExternalId: user.AccountingEmployeeId,
                CustomerExternalId: customerExternalId,
                Hours: entry.DurationMinutes / 60m,
                Date: entry.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Description: entry.Notes ?? $"Time entry for {entry.Category ?? "general"}",
                ServiceItemExternalId: null);

            var payload = JsonSerializer.Serialize(activity);
            await syncQueue.EnqueueAsync("TimeEntry", entry.Id, "CreateTimeActivity", payload, cancellationToken);
            logger.LogInformation("Enqueued CreateTimeActivity sync for TimeEntry {TimeEntryId}", entry.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue time activity sync for TimeEntry {TimeEntryId} — continuing", entry.Id);
        }
    }
}
