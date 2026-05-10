using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.OutreachPreferences;

/// <summary>
/// Phase 1r / Batch 1 follow-up — list of leads with active suppression
/// (any opt-out flag set, or CooldownUntil in the future). Drives the
/// /leads/suppression page so operators can see who's blocked and why.
/// </summary>
public record ListSuppressedLeadsQuery() : IRequest<List<SuppressedLeadSummaryModel>>;

public class ListSuppressedLeadsHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ListSuppressedLeadsQuery, List<SuppressedLeadSummaryModel>>
{
    public async Task<List<SuppressedLeadSummaryModel>> Handle(ListSuppressedLeadsQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Pull prefs rows that carry at least one suppression signal; join Lead
        // for display fields. Inactive (no opt-out and no active cooldown)
        // rows are filtered out — they're harmless to leave behind, no point
        // surfacing them.
        var rows = await (
            from p in db.LeadOutreachPreferences.AsNoTracking()
            join l in db.Leads.AsNoTracking() on p.LeadId equals l.Id
            where p.EmailOptOut || p.CallOptOut || p.SmsOptOut || (p.CooldownUntil != null && p.CooldownUntil > now)
            orderby p.UpdatedAt descending
            select new SuppressedLeadSummaryModel(
                l.Id, l.CompanyName, l.ContactName, l.Email, l.Phone,
                p.EmailOptOut, p.CallOptOut, p.SmsOptOut,
                p.CooldownUntil, p.CooldownReasonCode, p.UpdatedAt)
        ).ToListAsync(ct);

        return rows;
    }
}
