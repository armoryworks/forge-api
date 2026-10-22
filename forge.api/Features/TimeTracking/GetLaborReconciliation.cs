using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Costing;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record GetLaborReconciliationQuery(DateOnly From, DateOnly To, int? UserId = null)
    : IRequest<List<LaborReconciliationRowResponseModel>>;

public class GetLaborReconciliationValidator : AbstractValidator<GetLaborReconciliationQuery>
{
    public const int MaxDays = 62;

    public GetLaborReconciliationValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .WithMessage("The end date must be on or after the start date.");
        RuleFor(x => x)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaxDays)
            .WithMessage($"Choose at most {MaxDays} days.");
    }
}

public class GetLaborReconciliationHandler(
    AppDbContext db,
    ISettingsService settings,
    IClockEventTypeService clockEventTypes,
    IClock clock) : IRequestHandler<GetLaborReconciliationQuery, List<LaborReconciliationRowResponseModel>>
{
    public const decimal LongTimerMinutes = 600m;
    private static readonly TimeSpan MaxStretch = TimeSpan.FromHours(ClockStateRules.OpenFromPriorShiftHours);

    public async Task<List<LaborReconciliationRowResponseModel>> Handle(
        GetLaborReconciliationQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var rangeStart = ClockStateRules.LocalMidnightUtc(zone, request.From.ToDateTime(TimeOnly.MinValue));
        var rangeEnd = ClockStateRules.LocalMidnightUtc(zone, request.To.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var entryQuery = db.TimeEntries.AsNoTracking().Where(t => t.Date >= request.From && t.Date <= request.To);
        var eventQuery = db.ClockEvents.AsNoTracking().Where(e => e.Timestamp >= rangeStart - MaxStretch && e.Timestamp < rangeEnd);
        if (request.UserId is int onlyUser)
        {
            entryQuery = entryQuery.Where(t => t.UserId == onlyUser);
            eventQuery = eventQuery.Where(e => e.UserId == onlyUser);
        }

        var entries = await entryQuery
            .Select(t => new
            {
                t.UserId, t.Date, t.JobId, t.IsManual, t.TimerStart, t.TimerStop, t.DurationMinutes, t.LaborMinutes,
            })
            .ToListAsync(ct);
        var events = await eventQuery.ToListAsync(ct);
        var entriesByDay = entries.ToLookup(e => (e.UserId, e.Date));
        var eventsByUser = events.ToLookup(e => e.UserId);

        var userIds = entries.Select(e => e.UserId).Concat(events.Select(e => e.UserId)).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.LastName + ", " + u.FirstName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var definitions = await clockEventTypes.GetAllAsync(ct);
        var breaksPause = await settings.GetBoolAsync(ShopFloorSettings.BreaksPauseLaborKey, ct);

        var rows = new List<LaborReconciliationRowResponseModel>();
        foreach (var userId in userIds)
        {
            var marks = eventsByUser[userId]
                .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
                .Select(e => new ClockMark(e.Timestamp, StateOf(ClockStateRules.ResolveStatus(e, definitions).Status)))
                .ToList();

            for (var day = request.From; day <= request.To; day = day.AddDays(1))
            {
                var dayStart = ClockStateRules.LocalMidnightUtc(zone, day.ToDateTime(TimeOnly.MinValue));
                var dayEnd = ClockStateRules.LocalMidnightUtc(zone, day.AddDays(1).ToDateTime(TimeOnly.MinValue));
                if (dayStart >= now)
                    break;

                var dayEntries = entriesByDay[(userId, day)].ToList();
                var prior = marks.LastOrDefault(m => m.At < dayStart);
                var usesClock = marks.Any(m => m.At >= dayStart && m.At < dayEnd)
                    || (prior is not null && prior.State != ClockState.Out && dayStart - prior.At < MaxStretch);
                if (dayEntries.Count == 0 && !usesClock)
                    continue;

                var until = dayEnd < now ? dayEnd : now;
                var paidSpans = PaidTimeline.Build(marks, dayStart, until, breaksPause, MaxStretch, usesClock);
                decimal? paid = paidSpans is null
                    ? null
                    : Math.Round(paidSpans.Sum(s => (decimal)(s.End - s.Start).TotalMinutes), 2);

                var timers = dayEntries.Where(e => !e.IsManual && e.TimerStart.HasValue).ToList();
                var stopped = timers.Where(e => e.TimerStop.HasValue).ToList();
                var running = timers.Where(e => !e.TimerStop.HasValue).ToList();
                var jobLabor = stopped.Where(e => e.JobId.HasValue).Sum(e => e.LaborMinutes ?? e.DurationMinutes);
                var indirect = stopped.Where(e => !e.JobId.HasValue).Sum(e => e.LaborMinutes ?? e.DurationMinutes);
                var manual = dayEntries.Where(e => e.IsManual || !e.TimerStart.HasValue).Sum(e => (decimal)e.DurationMinutes);

                var spans = timers
                    .Select(e => (Start: e.TimerStart!.Value, End: e.TimerStop ?? now))
                    .Where(s => s.End > s.Start)
                    .ToList();
                var elapsed = spans.Sum(s => (decimal)(s.End - s.Start).TotalMinutes);
                var overlap = Math.Round(elapsed - UnionMinutes(spans), 2);
                var longTimers = spans.Count(s => (decimal)(s.End - s.Start).TotalMinutes >= LongTimerMinutes);

                rows.Add(new LaborReconciliationRowResponseModel(
                    userId,
                    names.GetValueOrDefault(userId, string.Empty),
                    day,
                    paid,
                    Math.Round(jobLabor, 2),
                    Math.Round(indirect, 2),
                    manual,
                    paid is decimal p ? Math.Round(p - jobLabor - indirect, 2) : null,
                    overlap,
                    longTimers,
                    running.Count));
            }
        }

        return rows.OrderBy(r => r.Date).ThenBy(r => r.UserName).ToList();
    }

    private static decimal UnionMinutes(List<(DateTimeOffset Start, DateTimeOffset End)> spans)
    {
        var total = 0m;
        DateTimeOffset? runStart = null;
        DateTimeOffset runEnd = default;
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (runStart is null || span.Start > runEnd)
            {
                if (runStart is not null)
                    total += (decimal)(runEnd - runStart.Value).TotalMinutes;
                runStart = span.Start;
                runEnd = span.End;
            }
            else if (span.End > runEnd)
                runEnd = span.End;
        }
        if (runStart is not null)
            total += (decimal)(runEnd - runStart.Value).TotalMinutes;
        return total;
    }

    private static ClockState StateOf(string status)
        => status == ClockStateRules.StatusIn ? ClockState.In
            : status == ClockStateRules.StatusOut ? ClockState.Out
            : ClockState.Break;
}
