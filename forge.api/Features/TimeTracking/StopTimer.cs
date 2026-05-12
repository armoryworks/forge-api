using System.Security.Claims;
using System.Text.Json;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Forge.Api.Hubs;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record StopTimerCommand(StopTimerRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class StopTimerHandler(
    ITimeTrackingRepository repo,
    IHttpContextAccessor httpContext,
    IHubContext<TimerHub> timerHub,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    UserManager<ApplicationUser> userManager,
    IJobRepository jobRepository,
    ICustomerRepository customerRepository,
    ILogger<StopTimerHandler> logger) : IRequestHandler<StopTimerCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(StopTimerCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var active = await repo.GetActiveTimerAsync(userId, cancellationToken);
        if (active is null)
            throw new InvalidOperationException("No active timer found.");

        var now = DateTimeOffset.UtcNow;
        active.TimerStop = now;
        active.DurationMinutes = (int)(now - active.TimerStart!.Value).TotalMinutes;
        if (!string.IsNullOrWhiteSpace(request.Data.Notes))
            active.Notes = request.Data.Notes.Trim();

        await repo.SaveChangesAsync(cancellationToken);

        var result = (await repo.GetTimeEntryByIdAsync(active.Id, cancellationToken))!;

        // Broadcast to all tabs for this user
        await timerHub.Clients.Group($"user:{userId}")
            .SendAsync("timerStopped", new TimerStoppedEvent(userId, result), cancellationToken);

        // Enqueue QB time activity if accounting is connected and employee is linked
        try
        {
            var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
            if (accountingService is not null)
            {
                var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
                if (syncStatus.Connected)
                {
                    var user = await userManager.FindByIdAsync(userId.ToString());
                    if (user?.AccountingEmployeeId is not null)
                    {
                        // Resolve customer external ID if job is linked to a customer
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

                        var hours = active.DurationMinutes / 60m;
                        var activity = new AccountingTimeActivity(
                            EmployeeExternalId: user.AccountingEmployeeId,
                            CustomerExternalId: customerExternalId,
                            Hours: hours,
                            Date: active.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                            Description: active.Notes ?? $"Time entry for {active.Category ?? "general"}",
                            ServiceItemExternalId: null);

                        var payload = JsonSerializer.Serialize(activity);
                        await syncQueue.EnqueueAsync("TimeEntry", active.Id, "CreateTimeActivity", payload, cancellationToken);
                        logger.LogInformation("Enqueued CreateTimeActivity sync for TimeEntry {TimeEntryId}", active.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue time activity sync for TimeEntry {TimeEntryId} — continuing", active.Id);
        }

        return result;
    }
}
