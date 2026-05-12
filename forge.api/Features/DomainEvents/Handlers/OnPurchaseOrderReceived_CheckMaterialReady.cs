using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnPurchaseOrderReceived_CheckMaterialReady(
    AppDbContext db,
    IClock clock,
    IHubContext<NotificationHub> notificationHub,
    ILogger<OnPurchaseOrderReceived_CheckMaterialReady> logger)
    : INotificationHandler<PurchaseOrderReceivedEvent>
{
    public async Task Handle(PurchaseOrderReceivedEvent notification, CancellationToken ct)
    {
        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .Include(p => p.Job)
                .ThenInclude(j => j!.Part)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == notification.PurchaseOrderId, ct);

        if (po?.JobId is null) return;

        var job = po.Job!;
        var jobNumber = job.JobNumber ?? po.JobId.ToString();

        // Get part IDs received in this PO
        var receivedPartIds = po.Lines
            .Select(l => l.PartId)
            .ToHashSet();

        // Check BOM entries with SourceType=Buy that match received parts
        // If the job has a part, check its BOM for buy-type entries
        if (job.PartId.HasValue)
        {
            var buyBomEntries = await db.BOMEntries
                .Where(b => b.ParentPartId == job.PartId.Value && b.SourceType == BOMSourceType.Buy)
                .AsNoTracking()
                .ToListAsync(ct);

            if (buyBomEntries.Count > 0)
            {
                // Get all PO lines for this job and check which BOM parts have been fully received
                var allPoLinesForJob = await db.PurchaseOrders
                    .Where(p => p.JobId == po.JobId)
                    .SelectMany(p => p.Lines)
                    .AsNoTracking()
                    .ToListAsync(ct);

                var receivedByPart = allPoLinesForJob
                    .GroupBy(l => l.PartId)
                    .ToDictionary(g => g.Key, g => g.All(l => l.ReceivedQuantity >= l.OrderedQuantity));

                // Check if all BOM buy-type materials have been fully received
                var allBomMaterialsReady = buyBomEntries.All(bom =>
                    receivedByPart.TryGetValue(bom.ChildPartId, out var received) && received);

                if (!allBomMaterialsReady) return;
            }
        }
        else
        {
            // No part on job — fall back to checking all PO lines for this job
            var allPosForJob = await db.PurchaseOrders
                .Include(p => p.Lines)
                .Where(p => p.JobId == po.JobId)
                .AsNoTracking()
                .ToListAsync(ct);

            var allFullyReceived = allPosForJob
                .SelectMany(p => p.Lines)
                .All(line => line.ReceivedQuantity >= line.OrderedQuantity);

            if (!allFullyReceived) return;
        }

        // Check if a materials-ready follow-up already exists
        var existingFollowUp = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "Job" &&
                f.SourceEntityId == po.JobId &&
                f.TriggerType == FollowUpTriggerType.MaterialsReady &&
                f.Status == FollowUpStatus.Open, ct);

        if (existingFollowUp) return;

        // Assign to the job's assignee, or a manager
        var assigneeId = job.AssigneeId ?? 0;
        if (assigneeId == 0)
        {
            assigneeId = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .Where(x => x.Name == "Manager" || x.Name == "Admin")
                .Select(x => x.UserId)
                .FirstOrDefaultAsync(ct);
        }

        if (assigneeId == 0) return;

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"Materials ready for JOB-{jobNumber}",
            Description = $"All purchase orders for Job {jobNumber} have been fully received. Production can begin.",
            AssignedToUserId = assigneeId,
            DueDate = clock.UtcNow.AddDays(1),
            SourceEntityType = "Job",
            SourceEntityId = po.JobId.Value,
            TriggerType = FollowUpTriggerType.MaterialsReady,
            Status = FollowUpStatus.Open,
        });

        // Create notification for the production lead
        db.Notifications.Add(new Notification
        {
            UserId = assigneeId,
            Type = "materials_ready",
            Severity = "info",
            Source = "purchasing",
            Title = "Materials Ready",
            Message = $"All materials for JOB-{jobNumber} are now available.",
            EntityType = "Job",
            EntityId = po.JobId.Value,
            SenderId = notification.UserId,
        });

        await db.SaveChangesAsync(ct);

        // Push via SignalR
        await notificationHub.Clients.Group($"user:{assigneeId}")
            .SendAsync("notificationReceived", new { type = "materials_ready", jobId = po.JobId.Value }, ct);

        logger.LogInformation("Materials ready for Job {JobNumber} (ID: {JobId}) — all BOM buy-type materials received",
            jobNumber, po.JobId);
    }
}
