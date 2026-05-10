using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.OutreachPreferences;

/// <summary>
/// Same upsert mechanic as <c>UpdateLeadOutreachPreferences</c> — see
/// that handler for the shape rationale. Activity-log indexing point is
/// (Contact, contactId), and additionally (Customer, customerId) so the
/// customer detail's activity feed surfaces the change as well (the
/// customer is the parent indexing point per the activity-logging rules
/// in CLAUDE.md — definitional master-data on a contact's marketing
/// preferences).
/// </summary>
public record UpdateContactOutreachPreferencesCommand(
    int ContactId,
    UpdateOutreachPreferencesRequest Patch) : IRequest<OutreachPreferencesResponseModel>;

public class UpdateContactOutreachPreferencesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<UpdateContactOutreachPreferencesCommand, OutreachPreferencesResponseModel>
{
    public async Task<OutreachPreferencesResponseModel> Handle(UpdateContactOutreachPreferencesCommand request, CancellationToken ct)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == request.ContactId, ct)
            ?? throw new KeyNotFoundException($"Contact {request.ContactId} not found.");

        var prefs = await db.ContactOutreachPreferences
            .FirstOrDefaultAsync(p => p.ContactId == request.ContactId, ct);

        var isNew = prefs is null;
        if (prefs is null)
        {
            prefs = new ContactOutreachPreferences { ContactId = request.ContactId };
            db.ContactOutreachPreferences.Add(prefs);
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
                ("Contact", request.ContactId),
                ("Customer", contact.CustomerId));
        }

        await db.SaveChangesAsync(ct);

        return new OutreachPreferencesResponseModel(
            Id: prefs.Id,
            OwnerId: prefs.ContactId,
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
