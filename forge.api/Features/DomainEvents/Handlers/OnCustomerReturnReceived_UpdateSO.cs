using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnCustomerReturnReceived_UpdateSO(
    AppDbContext db,
    IClock clock,
    ILogger<OnCustomerReturnReceived_UpdateSO> logger)
    : INotificationHandler<CustomerReturnReceivedEvent>
{
    public async Task Handle(CustomerReturnReceivedEvent notification, CancellationToken ct)
    {
        var customerReturn = await db.CustomerReturns
            .Include(r => r.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == notification.ReturnId, ct);

        if (customerReturn is null) return;

        // Find quality manager or admin to assign inspection
        var qualityManagerId = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager")
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

        if (qualityManagerId == 0) return;

        // Check for existing follow-up
        var exists = await db.FollowUpTasks
            .AnyAsync(f =>
                f.SourceEntityType == "CustomerReturn" &&
                f.SourceEntityId == customerReturn.Id &&
                f.TriggerType == FollowUpTriggerType.ReturnReceived &&
                f.Status == FollowUpStatus.Open, ct);

        if (exists) return;

        db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = $"Inspect return — {customerReturn.ReturnNumber}",
            Description = $"Customer return {customerReturn.ReturnNumber} from {customerReturn.Customer?.Name ?? "Unknown"} received. Reason: {customerReturn.Reason}. Inspect and determine disposition.",
            AssignedToUserId = qualityManagerId,
            DueDate = clock.UtcNow.AddDays(2),
            SourceEntityType = "CustomerReturn",
            SourceEntityId = customerReturn.Id,
            TriggerType = FollowUpTriggerType.ReturnReceived,
            Status = FollowUpStatus.Open,
        });

        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "CustomerReturn",
            EntityId = customerReturn.Id,
            UserId = notification.UserId,
            Action = "return_received",
            Description = $"Customer return {customerReturn.ReturnNumber} received. Inspection follow-up created.",
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created inspection follow-up for return {ReturnNumber}", customerReturn.ReturnNumber);
    }
}
