using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.OutreachPreferences;

/// <summary>
/// Returns the lead's preferences row, or null when the lead has none
/// (most leads). Caller treats null as "no opt-outs, no cooldown" — the
/// row's absence is semantically equivalent to all-false / no-cooldown.
/// </summary>
public record GetLeadOutreachPreferencesQuery(int LeadId) : IRequest<OutreachPreferencesResponseModel?>;

public class GetLeadOutreachPreferencesHandler(AppDbContext db)
    : IRequestHandler<GetLeadOutreachPreferencesQuery, OutreachPreferencesResponseModel?>
{
    public async Task<OutreachPreferencesResponseModel?> Handle(GetLeadOutreachPreferencesQuery request, CancellationToken ct)
    {
        var prefs = await db.LeadOutreachPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.LeadId == request.LeadId, ct);

        if (prefs is null) return null;

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
