using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.StatusTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderCancelled_HoldJobs(
    AppDbContext db,
    IMediator mediator,
    IHubContext<BoardHub> boardHub,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnSalesOrderCancelled_HoldJobs> logger)
    : INotificationHandler<SalesOrderCancelledEvent>
{
    private const string JobEntityType = "Job";
    private const string HoldStatusCode = "job_hold_customer";

    public async Task Handle(SalesOrderCancelledEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Customer)
            .Include(s => s.Lines)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null) return;

        var openLineIds = so.Lines.Where(l => !l.IsFullyShipped).Select(l => l.Id).ToList();
        if (openLineIds.Count == 0) return;

        var jobs = await db.Jobs
            .AsNoTracking()
            .Where(j => j.SalesOrderLineId.HasValue && openLineIds.Contains(j.SalesOrderLineId.Value)
                        && !j.IsArchived && j.CompletedDate == null && j.Disposition == null)
            .Select(j => new { j.Id, j.JobNumber, j.TrackTypeId })
            .ToListAsync(ct);

        if (jobs.Count == 0) return;

        var jobIds = jobs.Select(j => j.Id).ToList();
        var alreadyHeld = (await db.StatusEntries
            .Where(se => se.EntityType == JobEntityType && jobIds.Contains(se.EntityId)
                         && se.Category == "hold" && se.StatusCode == HoldStatusCode && se.EndedAt == null)
            .Select(se => se.EntityId)
            .ToListAsync(ct))
            .ToHashSet();

        var reason = $"Sales order {so.OrderNumber} cancelled";
        var held = jobs.Where(j => !alreadyHeld.Contains(j.Id)).ToList();

        foreach (var job in held)
        {
            await mediator.Send(new AddHoldCommand(JobEntityType, job.Id,
                new AddHoldRequestModel(HoldStatusCode, reason)), ct);
        }

        if (held.Count == 0) return;

        foreach (var job in held)
        {
            var detail = await mediator.Send(new GetJobByIdQuery(job.Id), ct);
            var evt = new BoardJobUpdatedEvent(job.Id, detail);
            await boardHub.Clients.Group($"board:{job.TrackTypeId}").SendAsync("jobUpdated", evt, ct);
            await boardHub.Clients.Group($"job:{job.Id}").SendAsync("jobUpdated", evt, ct);
        }

        var managerUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        var jobNumbers = string.Join(", ", held.Select(j => j.JobNumber));
        foreach (var managerId in managerUserIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = managerId,
                Type = "sales_order_cancelled",
                Severity = "warning",
                Source = "sales_orders",
                Title = "Sales Order Cancelled",
                Message = $"{so.OrderNumber} for {so.Customer?.Name ?? "Unknown"} was cancelled. {held.Count} open job(s) placed on hold: {jobNumbers}.",
                EntityType = "SalesOrder",
                EntityId = so.Id,
                SenderId = db.CurrentUserId,
            });
        }

        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = so.Id,
            UserId = db.CurrentUserId,
            Action = "jobs-held",
            Description = $"{held.Count} open job(s) placed on hold after Sales Order {so.OrderNumber} was cancelled.",
        });

        await db.SaveChangesAsync(ct);

        foreach (var managerId in managerUserIds)
        {
            await notificationHub.Clients.Group($"user:{managerId}")
                .SendAsync("notificationReceived", new { type = "sales_order_cancelled", salesOrderId = so.Id }, ct);
        }

        logger.LogInformation("Placed {Count} job(s) on hold for cancelled SO {OrderNumber}", held.Count, so.OrderNumber);
    }
}
