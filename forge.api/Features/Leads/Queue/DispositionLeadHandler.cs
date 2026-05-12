using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Leads.Queue;

/// <summary>
/// Phase 1r / Batch 6 — single-keystroke disposition handler. Worker
/// completes a touch attempt by submitting the next OutreachState +
/// optional notes; handler updates the lead, emits an activity-log
/// row, and (when the disposition implies engagement) advances the
/// LeadStatus to Contacted so the lead exits the high-volume queue.
///
/// State machine:
///   InProgress → NoAnswer / VoicemailLeft → re-queues for retry
///   InProgress → Engaged → Status flips to Contacted, exits queue
///   InProgress → CallbackScheduled → schedules + holds (uses FollowUpDate)
///   InProgress → BadData → exits queue + marks lead Lost (BadData reason)
///   InProgress → Suppressed → exits queue + future suppression takes over
/// </summary>
public record DispositionLeadCommand(int LeadId, DispositionLeadRequest Request) : IRequest;

public class DispositionLeadHandler(AppDbContext db)
    : IRequestHandler<DispositionLeadCommand>
{
    public async Task Handle(DispositionLeadCommand request, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new KeyNotFoundException($"Lead {request.LeadId} not found.");

        var r = request.Request;
        var prevState = lead.OutreachState;
        lead.OutreachState = r.NextState;

        // Side-effects per disposition type
        switch (r.NextState)
        {
            case OutreachState.NoAnswer:
            case OutreachState.VoicemailLeft:
                // Stays available for retry — flip back to Queued so the
                // next pull serves it after others.
                lead.OutreachState = OutreachState.Queued;
                break;
            case OutreachState.Engaged:
                // Lead exits high-volume queue; sales rep takes over via
                // the funnel. Status advances to Contacted (idempotent).
                if (lead.Status == LeadStatus.New) lead.Status = LeadStatus.Contacted;
                break;
            case OutreachState.CallbackScheduled:
                if (r.CallbackAt.HasValue) lead.FollowUpDate = r.CallbackAt;
                break;
            case OutreachState.BadData:
                lead.Status = LeadStatus.Lost;
                lead.LostReason = string.IsNullOrWhiteSpace(r.Notes) ? "Bad data" : $"Bad data — {r.Notes}";
                break;
            case OutreachState.Suppressed:
                // Operator-initiated suppression — the LeadOutreachPreferences
                // table is the authoritative store. This handler doesn't
                // create a prefs row; the UI flow walks the operator to
                // the suppression endpoint after this disposition.
                break;
        }

        var summary = $"Disposition: {prevState} → {r.NextState}" +
            (string.IsNullOrWhiteSpace(r.Notes) ? "" : $" — {r.Notes}");
        db.LogActivityAt("queue-disposition", summary, ("Lead", lead.Id));

        await db.SaveChangesAsync(ct);
    }
}
