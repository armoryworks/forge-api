using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models.PieceRates;
using Forge.Data.Context;

namespace Forge.Api.Features.PieceRates;

/// <summary>
/// The FLSA weekly make-up check over piece workers: for each worker with piece
/// entries in the workweek, compare piece earnings against hours worked × the
/// jurisdiction minimum wage (worker's work-location state, else the default
/// company location, else the federal floor) and report the make-up owed.
/// Assistance, not legal advice — simple weekly averaging only; stricter state
/// schemes (e.g. CA AB 1513 nonproductive-time rules) need their own handling.
/// V1 treats piece workers as piece-paid only: hourly pay recorded elsewhere is
/// not netted against the floor.
/// </summary>
[RequiresCapability("CAP-HR-PIECE-RATES")]
public record GetPieceRateComplianceQuery(DateOnly WeekStart) : IRequest<PieceRateComplianceModel>;

public class GetPieceRateComplianceHandler(AppDbContext db)
    : IRequestHandler<GetPieceRateComplianceQuery, PieceRateComplianceModel>
{
    public async Task<PieceRateComplianceModel> Handle(GetPieceRateComplianceQuery request, CancellationToken ct)
    {
        var weekStart = request.WeekStart;
        var weekEnd = weekStart.AddDays(6);

        var earningsByUser = await db.PieceWorkEntries.AsNoTracking()
            .Where(e => e.WorkDate >= weekStart && e.WorkDate <= weekEnd)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Earnings = g.Sum(e => e.Earnings) })
            .ToDictionaryAsync(x => x.UserId, x => x.Earnings, ct);

        if (earningsByUser.Count == 0)
            return new PieceRateComplianceModel(weekStart, weekEnd, [], 0m);

        var userIds = earningsByUser.Keys.ToList();

        var minutesByUser = await db.TimeEntries.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId) && t.Date >= weekStart && t.Date <= weekEnd)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, Minutes = g.Sum(t => t.DurationMinutes) })
            .ToDictionaryAsync(x => x.UserId, x => x.Minutes, ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.WorkLocationId })
            .ToListAsync(ct);

        var locations = await db.CompanyLocations.AsNoTracking()
            .Select(l => new { l.Id, l.State, l.IsDefault })
            .ToListAsync(ct);
        var defaultState = locations.FirstOrDefault(l => l.IsDefault)?.State;
        var stateByLocation = locations.ToDictionary(l => l.Id, l => l.State);

        // Minimum wages in force at week end, keyed by state (null key = federal floor).
        var wageRows = await db.MinimumWageRates.AsNoTracking()
            .Where(w => w.EffectiveFrom <= weekEnd && (w.EffectiveTo == null || w.EffectiveTo >= weekEnd))
            .OrderByDescending(w => w.EffectiveFrom)
            .ToListAsync(ct);
        decimal WageFor(string? state)
        {
            var row = wageRows.FirstOrDefault(w => w.StateCode == state)
                   ?? wageRows.FirstOrDefault(w => w.StateCode == null);
            var stateRate = row?.RatePerHour ?? 7.25m;
            var federal = wageRows.FirstOrDefault(w => w.StateCode == null)?.RatePerHour ?? 7.25m;
            // A state can never undercut the federal floor.
            return Math.Max(stateRate, federal);
        }

        var rows = new List<PieceRateComplianceRowModel>();
        foreach (var user in users.OrderBy(u => u.LastName).ThenBy(u => u.FirstName))
        {
            var state = user.WorkLocationId is int loc && stateByLocation.TryGetValue(loc, out var s)
                ? s
                : defaultState;
            state = string.IsNullOrWhiteSpace(state) ? null : state;

            var wage = WageFor(state);
            var hours = Math.Round(minutesByUser.GetValueOrDefault(user.Id) / 60m, 2);
            var earnings = earningsByUser[user.Id];
            var required = Math.Round(hours * wage, 2);
            var makeup = Math.Max(0m, required - earnings);

            rows.Add(new PieceRateComplianceRowModel(
                user.Id,
                $"{user.LastName}, {user.FirstName}".Trim(' ', ','),
                state,
                wage,
                hours,
                earnings,
                required,
                makeup,
                hours > 0 ? Math.Round(earnings / hours, 2) : 0m));
        }

        return new PieceRateComplianceModel(weekStart, weekEnd, rows, rows.Sum(r => r.MakeupOwed));
    }
}
