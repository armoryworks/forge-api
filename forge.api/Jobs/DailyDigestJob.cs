using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class DailyDigestJob(
    AppDbContext db,
    IIntegrationOutboxService outbox,
    ISystemSettingRepository settings,
    IClock clock,
    ILogger<DailyDigestJob> logger)
{
    public async Task SendDailyDigestAsync(CancellationToken ct = default)
    {
        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        // Get users with email digest enabled
        var users = await db.Users
            .Where(u => u.IsActive && u.Email != null)
            .Select(u => new { u.Id, u.Email, u.FirstName })
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var yesterday = now.AddDays(-1);

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Jobs assigned to user due in next 3 days
                var upcomingJobs = await db.Jobs
                    .Where(j => j.AssigneeId == user.Id && !j.IsArchived
                        && j.DueDate.HasValue && j.DueDate.Value <= now.AddDays(3)
                        && j.CompletedDate == null)
                    .OrderBy(j => j.DueDate)
                    .Select(j => new { j.JobNumber, j.Title, j.DueDate })
                    .Take(10)
                    .ToListAsync(ct);

                // Overdue jobs
                var overdueJobs = await db.Jobs
                    .Where(j => j.AssigneeId == user.Id && !j.IsArchived
                        && j.DueDate.HasValue && j.DueDate.Value < now
                        && j.CompletedDate == null)
                    .Select(j => new { j.JobNumber, j.Title, j.DueDate })
                    .Take(10)
                    .ToListAsync(ct);

                // Jobs completed yesterday
                var completedYesterday = await db.Jobs
                    .Where(j => j.AssigneeId == user.Id
                        && j.CompletedDate.HasValue && j.CompletedDate.Value >= yesterday)
                    .CountAsync(ct);

                if (upcomingJobs.Count == 0 && overdueJobs.Count == 0 && completedYesterday == 0)
                    continue;

                var html = EmailTemplateBuilder.BuildDigest(
                    companyName,
                    user.FirstName ?? "Team Member",
                    upcomingJobs.Select(j => new DigestJobItem(j.JobNumber, j.Title, j.DueDate)).ToList(),
                    overdueJobs.Select(j => new DigestJobItem(j.JobNumber, j.Title, j.DueDate)).ToList(),
                    completedYesterday);

                var operationKey = $"daily-digest:{user.Id}:{now:yyyy-MM-dd}";
                await outbox.EnqueueEmailAsync(
                    operationKey,
                    new EmailMessage(
                        user.Email!,
                        $"[{companyName}] Daily Digest — {now:MMMM d, yyyy}",
                        html),
                    entityType: "ApplicationUser",
                    entityId: user.Id,
                    ct: ct);

                logger.LogInformation("Daily digest queued for {Email}", user.Email);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send daily digest to {Email}", user.Email);
            }
        }
    }
}

public record DigestJobItem(string JobNumber, string Title, DateTimeOffset? DueDate);
