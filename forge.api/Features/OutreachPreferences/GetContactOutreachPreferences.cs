using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.OutreachPreferences;

public record GetContactOutreachPreferencesQuery(int ContactId) : IRequest<OutreachPreferencesResponseModel?>;

public class GetContactOutreachPreferencesHandler(AppDbContext db)
    : IRequestHandler<GetContactOutreachPreferencesQuery, OutreachPreferencesResponseModel?>
{
    public async Task<OutreachPreferencesResponseModel?> Handle(GetContactOutreachPreferencesQuery request, CancellationToken ct)
    {
        var prefs = await db.ContactOutreachPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ContactId == request.ContactId, ct);

        if (prefs is null) return null;

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
