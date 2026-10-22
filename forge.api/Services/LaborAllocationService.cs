using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Costing;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Services;

/// <summary>
/// Loads a person's timers and clock events, runs <see cref="ConcurrentLaborAllocator"/> and writes each entry's
/// labor share (<see cref="TimeEntry.LaborMinutes"/>), then re-prices it. Runs when a timer stops and when an edit
/// moves or removes a timer; never on a schedule, so an allocation stays put once written.
/// </summary>
public class LaborAllocationService(
    AppDbContext db,
    ISettingsService settings,
    IClockEventTypeService clockEventTypes,
    IClock clock,
    IJobCostService? jobCost = null) : ILaborAllocationService
{
    private static readonly TimeSpan MaxStretch = TimeSpan.FromHours(ClockStateRules.OpenFromPriorShiftHours);
    private const int DateMarginDays = 2;
    private const decimal RoundingTolerance = 0.01m;

    public async Task AllocateStoppedAsync(IReadOnlyList<TimeEntry> entries, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var weighted = await IsWeightedAsync(ct);
        foreach (var group in entries
            .Where(e => !e.IsManual && !e.IsLocked && e.TimerStart.HasValue && e.TimerStop.HasValue)
            .GroupBy(e => e.UserId))
        {
            var targets = group.ToList();
            if (!weighted)
            {
                foreach (var entry in targets)
                    Write(entry, null, LaborAllocationMethod.None, now);
                continue;
            }

            foreach (var empty in targets.Where(e => e.TimerStop <= e.TimerStart))
                Write(empty, null, LaborAllocationMethod.Weighted, now);
            targets = targets.Where(e => e.TimerStop > e.TimerStart).ToList();
            if (targets.Count == 0)
                continue;

            var from = targets.Min(e => e.TimerStart!.Value);
            var to = targets.Max(e => e.TimerStop!.Value);
            var context = await LoadAsync(group.Key, from, to, targets, now, ct);
            var results = ConcurrentLaborAllocator.Allocate(
                context.Timers, context.Paid, targets.Select(e => e.Id).ToHashSet());
            foreach (var result in results)
            {
                var entry = targets.First(e => e.Id == result.TimerId);
                Write(entry, result.Shared ? result.LaborMinutes : null, LaborAllocationMethod.Weighted, now);
            }
        }
    }

    public async Task ReallocateAsync(
        int userId,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> spans,
        IReadOnlyList<TimeEntry> edited,
        CancellationToken ct)
    {
        if (jobCost is not null)
            await jobCost.RepriceMachineSiblingsAsync(edited, ct);

        var valid = spans.Where(s => s.End > s.Start).ToList();
        if (valid.Count == 0)
            return;

        var now = clock.UtcNow;
        var from = valid.Min(s => s.Start);
        var to = valid.Max(s => s.End);
        var affected = (await LoadEntriesAsync(userId, from, to, edited, now, ct))
            .Where(e => e.TimerStop > e.TimerStart)
            .ToList();
        if (affected.Count == 0)
            return;

        var editedIds = edited.Select(e => e.Id).ToHashSet();
        affected = affected.Where(e => editedIds.Contains(e.Id) || e.AllocationMethod.HasValue).ToList();
        if (affected.Count == 0)
            return;

        var changed = await ApplyAsync(userId, affected, now, refuseLocked: true, editedIds, ct);
        await PriceAsync(changed, ct);
    }

    public async Task<LaborRecomputeResponseModel> RecomputeAsync(
        int? userId, DateOnly from, DateOnly to, bool apply, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var query = db.TimeEntries
            .Where(t => t.Date >= from && t.Date <= to
                && !t.IsManual && t.TimerStart != null && t.TimerStop != null && t.TimerStop > t.TimerStart);
        if (userId is int onlyUser)
            query = query.Where(t => t.UserId == onlyUser);
        var inRange = await query.ToListAsync(ct);

        var before = inRange.ToDictionary(e => e.Id, e => (Labor: LaborOf(e), e.LaborCost, e.BurdenCost));
        var lockedSkipped = inRange.Count(e => e.IsLocked);
        var changed = new List<TimeEntry>();
        foreach (var group in inRange.Where(e => !e.IsLocked).GroupBy(e => e.UserId))
            changed.AddRange(await ApplyAsync(group.Key, group.ToList(), now, refuseLocked: false, new HashSet<int>(), ct));

        await PriceAsync(inRange.Where(e => !e.IsLocked).ToList(), ct);

        var moved = inRange
            .Where(e => !e.IsLocked && e.JobId.HasValue)
            .Where(e => LaborOf(e) != before[e.Id].Labor
                || e.LaborCost != before[e.Id].LaborCost
                || e.BurdenCost != before[e.Id].BurdenCost)
            .ToList();

        var jobNumbers = await db.Jobs.AsNoTracking()
            .Where(j => moved.Select(e => e.JobId!.Value).Contains(j.Id))
            .Select(j => new { j.Id, j.JobNumber })
            .ToDictionaryAsync(j => j.Id, j => j.JobNumber, ct);

        var jobs = moved
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new LaborRecomputeJobChangeResponseModel(
                g.Key,
                jobNumbers.GetValueOrDefault(g.Key, string.Empty),
                g.Count(),
                g.Sum(e => before[e.Id].Labor),
                g.Sum(LaborOf),
                g.Sum(e => before[e.Id].LaborCost),
                g.Sum(e => e.LaborCost),
                g.Sum(e => before[e.Id].BurdenCost),
                g.Sum(e => e.BurdenCost)))
            .OrderBy(j => j.JobNumber)
            .ToList();

        var result = new LaborRecomputeResponseModel(
            apply,
            moved.Count + changed.Count(e => !e.JobId.HasValue),
            lockedSkipped,
            jobs.Sum(j => j.LaborMinutesBefore),
            jobs.Sum(j => j.LaborMinutesAfter),
            jobs.Sum(j => j.LaborCostBefore + j.BurdenCostBefore),
            jobs.Sum(j => j.LaborCostAfter + j.BurdenCostAfter),
            jobs);

        if (apply)
        {
            foreach (var job in jobs)
                db.LogActivityAt("labor-recomputed",
                    $"Recomputed labor on {job.EntriesChanged} time entr{(job.EntriesChanged == 1 ? "y" : "ies")} dated {from:MM/dd/yyyy}–{to:MM/dd/yyyy}: "
                        + $"{job.LaborMinutesBefore:0.##} → {job.LaborMinutesAfter:0.##} labor minutes, "
                        + $"cost {job.LaborCostBefore + job.BurdenCostBefore:0.00} → {job.LaborCostAfter + job.BurdenCostAfter:0.00}",
                    ("Job", job.JobId));
            await db.SaveChangesAsync(ct);
        }
        else
            db.ChangeTracker.Clear();

        return result;
    }

    private async Task<List<TimeEntry>> ApplyAsync(
        int userId, List<TimeEntry> targets, DateTimeOffset now, bool refuseLocked, IReadOnlySet<int> writableLocked,
        CancellationToken ct)
    {
        var weighted = await IsWeightedAsync(ct);
        var proposals = new Dictionary<int, (decimal? Labor, LaborAllocationMethod Method)>();
        if (!weighted)
        {
            foreach (var entry in targets)
                proposals[entry.Id] = (null, LaborAllocationMethod.None);
        }
        else
        {
            var from = targets.Min(e => e.TimerStart!.Value);
            var to = targets.Max(e => e.TimerStop!.Value);
            var context = await LoadAsync(userId, from, to, targets, now, ct);
            var results = ConcurrentLaborAllocator.Allocate(
                context.Timers, context.Paid, targets.Select(e => e.Id).ToHashSet());
            foreach (var result in results)
                proposals[result.TimerId] = (result.Shared ? result.LaborMinutes : null, LaborAllocationMethod.Weighted);
        }

        var changed = new List<TimeEntry>();
        foreach (var entry in targets)
        {
            if (!proposals.TryGetValue(entry.Id, out var proposal))
                continue;
            if (entry.LaborMinutes == proposal.Labor && entry.AllocationMethod == proposal.Method)
                continue;
            if (refuseLocked && !writableLocked.Contains(entry.Id) && entry.AllocationMethod == proposal.Method
                && Math.Abs(LaborOf(entry) - (proposal.Labor ?? entry.DurationMinutes)) <= RoundingTolerance)
                continue;

            if (entry.IsLocked && !writableLocked.Contains(entry.Id))
            {
                if (refuseLocked && entry.AllocationMethod == LaborAllocationMethod.Weighted && entry.LaborMinutes != proposal.Labor)
                    await RefuseLockedAsync(entry, ct);
                continue;
            }

            Write(entry, proposal.Labor, proposal.Method, now);
            changed.Add(entry);
        }

        return changed;
    }

    private async Task RefuseLockedAsync(TimeEntry entry, CancellationToken ct)
    {
        var lockedThrough = await TimeLockBoundary.GetAsync(db, ct) ?? entry.Date;
        throw new InvalidOperationException(
            $"Time on or before {lockedThrough:yyyy-MM-dd} is in a locked pay period and cannot be added or moved there. "
            + $"This change would re-split the labor of a locked time entry dated {entry.Date:yyyy-MM-dd}.");
    }

    private async Task PriceAsync(IReadOnlyList<TimeEntry> entries, CancellationToken ct)
    {
        if (jobCost is null)
            return;
        foreach (var entry in entries.Where(e => e.JobId.HasValue))
            await jobCost.PriceTimeEntryAsync(entry, ct);
    }

    private static void Write(TimeEntry entry, decimal? laborMinutes, LaborAllocationMethod method, DateTimeOffset now)
    {
        entry.LaborMinutes = laborMinutes;
        entry.AllocationMethod = method;
        entry.AllocatedAt = now;
    }

    private static decimal LaborOf(TimeEntry entry) => entry.LaborMinutes ?? entry.DurationMinutes;

    private async Task<bool> IsWeightedAsync(CancellationToken ct)
        => !string.Equals(
            await settings.GetStringAsync(ShopFloorSettings.LaborSplitKey, ct),
            ShopFloorSettings.LaborSplitNone,
            StringComparison.OrdinalIgnoreCase);

    private async Task<List<TimeEntry>> LoadEntriesAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<TimeEntry> extra, DateTimeOffset now, CancellationToken ct)
    {
        var fromDate = DateOnly.FromDateTime(from.UtcDateTime).AddDays(-DateMarginDays);
        var toDate = DateOnly.FromDateTime(to.UtcDateTime).AddDays(DateMarginDays);
        var loaded = await db.TimeEntries
            .Where(t => t.UserId == userId && t.Date >= fromDate && t.Date <= toDate
                && !t.IsManual && t.TimerStart != null && t.TimerStart < to
                && (t.TimerStop == null || t.TimerStop > from))
            .ToListAsync(ct);

        var byId = loaded.ToDictionary(e => e.Id);
        foreach (var entry in extra.Where(e => e.UserId == userId))
            byId[entry.Id] = entry;

        return byId.Values
            .Where(e => e.DeletedAt == null && !e.IsManual && e.TimerStart.HasValue
                && e.TimerStart.Value < to && (e.TimerStop ?? now) > from)
            .ToList();
    }

    private async Task<(List<LaborTimer> Timers, IReadOnlyList<PaidSpan> Paid)> LoadAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<TimeEntry> targets, DateTimeOffset now, CancellationToken ct)
    {
        var entries = await LoadEntriesAsync(userId, from, to, targets, now, ct);
        if (entries.Count == 0)
            return ([], []);
        var windowStart = entries.Min(e => e.TimerStart!.Value);
        var windowEnd = entries.Max(e => e.TimerStop ?? now);

        var operationIds = entries.Where(e => e.OperationId.HasValue).Select(e => e.OperationId!.Value).Distinct().ToList();
        var attendance = await db.Operations.AsNoTracking()
            .Where(o => operationIds.Contains(o.Id))
            .Select(o => new { o.Id, o.LaborAttendancePercent })
            .ToDictionaryAsync(o => o.Id, o => o.LaborAttendancePercent, ct);

        var timers = entries
            .Select(e => new LaborTimer(
                e.Id,
                e.TimerStart!.Value,
                e.TimerStop ?? (now > e.TimerStart!.Value ? now : e.TimerStart!.Value),
                Weight(e, attendance)))
            .ToList();

        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var firstDay = ClockStateRules.LocalToday(zone, windowStart);
        var lastDay = ClockStateRules.LocalToday(zone, windowEnd);
        var lookback = ClockStateRules.LocalMidnightUtc(zone, firstDay) - MaxStretch;
        var events = await db.ClockEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Timestamp >= lookback && e.Timestamp <= windowEnd)
            .ToListAsync(ct);

        var definitions = await clockEventTypes.GetAllAsync(ct);
        var marks = events
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new ClockMark(e.Timestamp, StateOf(ClockStateRules.ResolveStatus(e, definitions).Status)))
            .ToList();
        var breaksPause = await settings.GetBoolAsync(ShopFloorSettings.BreaksPauseLaborKey, ct);

        var paid = new List<PaidSpan>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var dayStart = ClockStateRules.LocalMidnightUtc(zone, day);
            var dayEnd = ClockStateRules.LocalMidnightUtc(zone, day.AddDays(1));
            var sliceStart = dayStart > windowStart ? dayStart : windowStart;
            var sliceEnd = dayEnd < windowEnd ? dayEnd : windowEnd;
            if (sliceEnd <= sliceStart)
                continue;

            var priorToDay = marks.LastOrDefault(m => m.At < dayStart);
            var usesClock = marks.Any(m => m.At >= dayStart && m.At < dayEnd)
                || (priorToDay is not null && priorToDay.State != ClockState.Out && dayStart - priorToDay.At < MaxStretch);
            paid.AddRange(PaidTimeline.Build(marks, sliceStart, sliceEnd, breaksPause, MaxStretch, usesClock) ?? [new PaidSpan(sliceStart, sliceEnd)]);
        }

        return (timers, paid);
    }

    private static decimal Weight(TimeEntry entry, IReadOnlyDictionary<int, decimal> attendance)
        => entry.EntryType == TimeEntryType.Run && entry.OperationId is int operationId
            && attendance.TryGetValue(operationId, out var percent)
            ? percent
            : 100m;

    private static ClockState StateOf(string status)
        => status == ClockStateRules.StatusIn ? ClockState.In
            : status == ClockStateRules.StatusOut ? ClockState.Out
            : ClockState.Break;
}
