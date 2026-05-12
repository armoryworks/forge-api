using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Weekly Hangfire job — finds I-9 submissions where work authorisation documents
/// are expiring within 90 days and sends notifications to Admin/Manager/OfficeManager.
/// </summary>
public class CheckI9ReverificationJob(
    AppDbContext db,
    IClock clock,
    ILogger<CheckI9ReverificationJob> logger)
{
    private static readonly TimeSpan WarningWindow = TimeSpan.FromDays(90);

    public async Task CheckReverificationDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var warningCutoff = now.Add(WarningWindow);

        // Documents overdue or expiring within 90 days
        var due = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .Where(s =>
                s.Template.FormType == ComplianceFormType.I9
                && s.I9Section2SignedAt != null
                && s.I9ReverificationDueAt != null
                && s.I9ReverificationDueAt <= warningCutoff)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            logger.LogInformation("[I9ReverificationJob] No upcoming reverification deadlines found");
            return;
        }

        logger.LogInformation(
            "[I9ReverificationJob] {Count} I-9 submission(s) with upcoming reverification", due.Count);

        // Fetch admin/manager user IDs to notify
        var notifyUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager" || x.Name == "OfficeManager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var submission in due)
        {
            var employee = await db.Users.FindAsync([submission.UserId], ct);
            if (employee is null) continue;

            var employeeName = $"{employee.LastName}, {employee.FirstName}";
            var isOverdue = submission.I9ReverificationDueAt!.Value <= now;
            var severity = isOverdue ? "critical" : "warning";
            var title = isOverdue ? "I-9 Reverification Overdue" : "I-9 Reverification Due Soon";
            var message = isOverdue
                ? $"{employeeName} — work authorisation expired {submission.I9ReverificationDueAt:MM/dd/yyyy}. Reverification required."
                : $"{employeeName} — work authorisation expires {submission.I9ReverificationDueAt:MM/dd/yyyy}. Reverification needed within 90 days.";

            foreach (var managerId in notifyUserIds)
            {
                // Avoid duplicate notifications within the same week
                var existing = await db.Set<Notification>()
                    .AsNoTracking()
                    .AnyAsync(n =>
                        n.UserId == managerId
                        && n.EntityType == "compliance_submissions"
                        && n.EntityId == submission.Id
                        && n.Type == "i9_reverification_due"
                        && n.CreatedAt >= now.AddDays(-7), ct);

                if (existing) continue;

                var notification = new Notification
                {
                    Type = "i9_reverification_due",
                    Severity = severity,
                    Source = "compliance",
                    Title = title,
                    Message = message,
                    EntityType = "compliance_submissions",
                    EntityId = submission.Id,
                    UserId = managerId,
                };
                db.Set<Notification>().Add(notification);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "[I9ReverificationJob] Processed reverification notifications for {Count} submission(s)", due.Count);
    }
}
