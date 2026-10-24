using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Api.Features.Notifications;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Phase 1j.5 — daily check for leads whose <c>FollowUpDate</c> falls on
/// today or tomorrow. For each match, creates one notification for the
/// lead's assigned rep (<c>AssignedToUserId</c>), falling back to the user
/// who created it when the lead has no owner, so they get a heads-up in the
/// bell + a digest line. A lead with neither is skipped.
///
/// De-duplicated per (lead, scheduled date) — re-running the job within
/// the same day doesn't spam the rep. The Notification table's existing
/// pattern (check-by-source-and-creation-window) is what we lean on.
///
/// Lost / Converted leads are skipped — their FollowUpDate is irrelevant.
/// </summary>
public class LeadFollowUpReminderJob(
    AppDbContext db,
    ISender mediator,
    IClock clock,
    ILogger<LeadFollowUpReminderJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var dayAfterTomorrow = todayStart.AddDays(2);

        // Active leads whose FollowUpDate is today or tomorrow.
        // (We deliberately scan a 2-day window so reps see "tomorrow's
        // follow-ups" at end-of-day; the dedup logic prevents duplicate
        // notifications.)
        var due = await db.Leads
            .Where(l => l.FollowUpDate != null
                && l.FollowUpDate >= todayStart
                && l.FollowUpDate < dayAfterTomorrow
                && l.Status != LeadStatus.Lost
                && l.Status != LeadStatus.Converted)
            .Select(l => new
            {
                l.Id, l.CompanyName, l.ContactName, l.FollowUpDate,
                RecipientId = l.AssignedToUserId ?? l.CreatedBy,
            })
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            logger.LogInformation("LeadFollowUpReminderJob: no leads due today/tomorrow");
            return;
        }

        var notified = 0;
        foreach (var lead in due)
        {
            ct.ThrowIfCancellationRequested();
            if (lead.RecipientId <= 0) continue;

            // Dedup: skip if we already notified this rep for this lead
            // since the day boundary. Re-run safety for in-day reruns.
            var alreadyNotified = await db.Notifications.AnyAsync(n =>
                n.EntityType == "Lead"
                && n.EntityId == lead.Id
                && n.Source == "lead-followup-due"
                && n.UserId == lead.RecipientId
                && n.CreatedAt >= todayStart, ct);
            if (alreadyNotified) continue;

            var isToday = lead.FollowUpDate < todayStart.AddDays(1);
            var when = isToday ? "today" : "tomorrow";
            var displayName = string.IsNullOrEmpty(lead.ContactName)
                ? lead.CompanyName
                : $"{lead.CompanyName} ({lead.ContactName})";

            try
            {
                await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                    UserId: lead.RecipientId,
                    Type: "alert",
                    Severity: "info",
                    Source: "lead-followup-due",
                    Title: $"Follow up {when}: {lead.CompanyName}",
                    Message: $"A follow-up with {displayName} is due {when}.",
                    EntityType: "Lead",
                    EntityId: lead.Id,
                    SenderId: null)), ct);
                notified++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "LeadFollowUpReminderJob: failed to notify user {UserId} about lead {LeadId}",
                    lead.RecipientId, lead.Id);
            }
        }

        logger.LogInformation(
            "LeadFollowUpReminderJob: scanned {Total} due leads, sent {Notified} notifications",
            due.Count, notified);
    }
}
