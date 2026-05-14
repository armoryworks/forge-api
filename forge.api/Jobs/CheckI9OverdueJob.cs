using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — finds I-9 submissions where Section 2 is past deadline and
/// sends notifications to Admin/Manager/OfficeManager roles.
/// </summary>
public class CheckI9OverdueJob(
    AppDbContext db,
    IClock clock,
    ILogger<CheckI9OverdueJob> logger)
{
    public async Task CheckOverdueSection2Async(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var overdue = await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .Where(s =>
                s.Template.FormType == ComplianceFormType.I9
                && s.I9Section1SignedAt != null
                && s.I9Section2SignedAt == null
                && s.I9Section2OverdueAt != null
                && s.I9Section2OverdueAt <= now)
            .ToListAsync(ct);

        if (overdue.Count == 0)
        {
            logger.LogInformation("[I9OverdueJob] No overdue Section 2 submissions found");
            return;
        }

        logger.LogWarning(
            "[I9OverdueJob] {Count} I-9 submission(s) with overdue Section 2", overdue.Count);

        // Fetch admin/manager user IDs to notify
        var notifyUserIds = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name == "Admin" || x.Name == "Manager" || x.Name == "OfficeManager")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var submission in overdue)
        {
            var employee = await db.Users.FindAsync([submission.UserId], ct);
            if (employee is null) continue;

            var employeeName = $"{employee.LastName}, {employee.FirstName}";

            foreach (var managerId in notifyUserIds)
            {
                var notification = new Notification
                {
                    Type = "i9_section2_overdue",
                    Severity = "critical",
                    Source = "compliance",
                    Title = "I-9 Section 2 Overdue",
                    Message = $"{employeeName} — Section 2 was due {submission.I9Section2OverdueAt:MM/dd/yyyy} and has not been completed.",
                    EntityType = "compliance_submissions",
                    EntityId = submission.Id,
                    UserId = managerId,
                };
                db.Set<Notification>().Add(notification);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("[I9OverdueJob] Sent overdue notifications for {Count} submission(s)", overdue.Count);
    }
}
