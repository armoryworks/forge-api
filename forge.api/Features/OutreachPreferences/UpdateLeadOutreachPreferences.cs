using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.OutreachPreferences;

/// <summary>
/// Upsert handler — auto-creates the preferences row on first non-null
/// field touch, then mutates it in place on subsequent calls. Emits a
/// rolled-up activity-log entry summarizing which channels / cooldown
/// fields changed (per the rollup rule in CLAUDE.md — one row per
/// command, not per field).
///
/// Each opt-out flag, when flipped on, stamps its corresponding
/// `*OptOutAt` timestamp using <see cref="IClock"/>. Flipping off
/// rescinds the opt-out by clearing both the flag and the timestamp.
/// Source strings are caller-provided (e.g. "Reply on 2026-05-10",
/// "Phone request from contact during call") so the audit trail
/// captures intent.
/// </summary>
public record UpdateLeadOutreachPreferencesCommand(
    int LeadId,
    UpdateOutreachPreferencesRequest Patch) : IRequest<OutreachPreferencesResponseModel>;

public class UpdateLeadOutreachPreferencesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpdateLeadOutreachPreferencesCommand, OutreachPreferencesResponseModel>
{
    public async Task<OutreachPreferencesResponseModel> Handle(UpdateLeadOutreachPreferencesCommand request, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct)
            ?? throw new KeyNotFoundException($"Lead {request.LeadId} not found.");

        var prefs = await db.LeadOutreachPreferences
            .FirstOrDefaultAsync(p => p.LeadId == request.LeadId, ct);

        var isNew = prefs is null;
        if (prefs is null)
        {
            prefs = new LeadOutreachPreferences { LeadId = request.LeadId };
            db.LeadOutreachPreferences.Add(prefs);
        }

        var changed = new List<string>();
        var p = request.Patch;
        var now = clock.UtcNow;

        if (p.EmailOptOut is { } eo && eo != prefs.EmailOptOut)
        {
            prefs.EmailOptOut = eo;
            prefs.EmailOptOutAt = eo ? now : null;
            prefs.EmailOptOutSource = eo ? p.EmailOptOutSource : null;
            changed.Add(eo ? "email-opt-out-set" : "email-opt-out-cleared");
        }
        if (p.CallOptOut is { } co && co != prefs.CallOptOut)
        {
            prefs.CallOptOut = co;
            prefs.CallOptOutAt = co ? now : null;
            prefs.CallOptOutSource = co ? p.CallOptOutSource : null;
            changed.Add(co ? "call-opt-out-set" : "call-opt-out-cleared");
        }
        if (p.SmsOptOut is { } so && so != prefs.SmsOptOut)
        {
            prefs.SmsOptOut = so;
            prefs.SmsOptOutAt = so ? now : null;
            prefs.SmsOptOutSource = so ? p.SmsOptOutSource : null;
            changed.Add(so ? "sms-opt-out-set" : "sms-opt-out-cleared");
        }

        if (p.CooldownUntil != prefs.CooldownUntil)
        {
            prefs.CooldownUntil = p.CooldownUntil;
            changed.Add(p.CooldownUntil is null ? "cooldown-cleared" : "cooldown-set");
        }
        if (p.CooldownReasonCode != null && p.CooldownReasonCode != prefs.CooldownReasonCode)
        {
            prefs.CooldownReasonCode = p.CooldownReasonCode;
        }
        if (p.CooldownNotes != null && p.CooldownNotes != prefs.CooldownNotes)
        {
            prefs.CooldownNotes = p.CooldownNotes;
        }

        if (changed.Count > 0)
        {
            var verb = isNew ? "outreach-preferences-created" : "outreach-preferences-updated";
            db.LogActivityAt(verb,
                $"Updated outreach preferences: {string.Join(", ", changed)}",
                ("Lead", request.LeadId));
        }

        await db.SaveChangesAsync(ct);

        return new OutreachPreferencesResponseModel(
            Id: prefs.Id,
            OwnerId: prefs.LeadId,
            EmailOptOut: prefs.EmailOptOut,
            EmailOptOutAt: prefs.EmailOptOutAt,
            EmailOptOutSource: prefs.EmailOptOutSource,
            CallOptOut: prefs.CallOptOut,
            CallOptOutAt: prefs.CallOptOutAt,
            CallOptOutSource: prefs.CallOptOutSource,
            SmsOptOut: prefs.SmsOptOut,
            SmsOptOutAt: prefs.SmsOptOutAt,
            SmsOptOutSource: prefs.SmsOptOutSource,
            CooldownUntil: prefs.CooldownUntil,
            CooldownReasonCode: prefs.CooldownReasonCode,
            CooldownNotes: prefs.CooldownNotes);
    }
}
