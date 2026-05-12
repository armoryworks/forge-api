using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class UninvoicedJobNudgeJob(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IMediator mediator,
    IClock clock,
    ILogger<UninvoicedJobNudgeJob> logger)
{
    public async Task NudgeUninvoicedJobsAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-3);

        var uninvoicedCount = await db.Jobs
            .Where(j => j.CompletedDate != null && !j.IsArchived)
            .Where(j => j.CompletedDate <= cutoff)
            .Where(j =>
                j.SalesOrderLineId == null ||
                !j.SalesOrderLine!.SalesOrder.Invoices.Any(i => i.DeletedAt == null))
            .CountAsync(ct);

        if (uninvoicedCount == 0)
        {
            logger.LogInformation("No uninvoiced completed jobs older than 3 days");
            return;
        }

        logger.LogInformation(
            "Found {Count} completed jobs older than 3 days without invoices — sending nudge notifications",
            uninvoicedCount);

        // Get Admin and Manager users to notify
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        var managers = await userManager.GetUsersInRoleAsync("Manager");

        var userIds = admins.Concat(managers)
            .Select(u => u.Id)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            logger.LogWarning("No Admin or Manager users found to send uninvoiced job nudge");
            return;
        }

        var message = uninvoicedCount == 1
            ? "1 completed job has not been invoiced (completed over 3 days ago)."
            : $"{uninvoicedCount} completed jobs have not been invoiced (completed over 3 days ago).";

        foreach (var userId in userIds)
        {
            await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                UserId: userId,
                Type: "nudge",
                Severity: "warning",
                Source: "invoicing",
                Title: "Uninvoiced Jobs",
                Message: message,
                EntityType: null,
                EntityId: null,
                SenderId: null)), ct);
        }

        logger.LogInformation(
            "Sent uninvoiced job nudge to {UserCount} Admin/Manager users",
            userIds.Count);
    }
}
