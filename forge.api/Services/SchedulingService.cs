using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Services;

public class SchedulingService(AppDbContext db, IClock clock, ILogger<SchedulingService> logger) : ISchedulingService
{
    public async Task<ScheduleRunResponseModel> ScheduleAsync(ScheduleParameters parameters, CancellationToken ct)
    {
        return await RunSchedulerAsync(parameters, isSimulation: false, ct);
    }

    public async Task<ScheduleRunResponseModel> SimulateAsync(ScheduleParameters parameters, CancellationToken ct)
    {
        return await RunSchedulerAsync(parameters, isSimulation: true, ct);
    }

    private async Task<ScheduleRunResponseModel> RunSchedulerAsync(ScheduleParameters parameters, bool isSimulation, CancellationToken ct)
    {
        // Check for concurrent runs
        var hasRunning = await db.ScheduleRuns
            .AnyAsync(r => r.Status == ScheduleRunStatus.Running, ct);
        if (hasRunning)
            throw new InvalidOperationException("A scheduling run is already in progress.");

        var run = new ScheduleRun
        {
            RunDate = clock.UtcNow,
            Direction = parameters.Direction,
            Status = ScheduleRunStatus.Running,
            RunByUserId = parameters.RunByUserId ?? 0,
            ParametersJson = System.Text.Json.JsonSerializer.Serialize(parameters),
        };
        db.ScheduleRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            // 1. Get jobs to schedule
            var jobsQuery = db.Jobs
                .AsNoTracking()
                .Include(j => j.Part)
                    .ThenInclude(p => p!.Operations.Where(o => o.DeletedAt == null))
                .Include(j => j.JobParts)
                .Include(j => j.SalesOrderLine)
                .Where(j => j.DeletedAt == null
                    && !j.IsArchived
                    && j.CompletedDate == null
                    && j.Disposition == null
                    && j.PartId != null
                    && j.Part!.Operations.Any(o => o.DeletedAt == null));

            if (parameters.JobIdFilter is { Length: > 0 })
                jobsQuery = jobsQuery.Where(j => parameters.JobIdFilter.Contains(j.Id));

            var jobs = await jobsQuery.ToListAsync(ct);

            // Sort jobs by priority rule
            jobs = SortByPriorityRule(jobs, parameters.PriorityRule);

            // 2. Get work centers with shifts and calendar overrides
            var workCenters = await db.WorkCenters
                .AsNoTracking()
                .WithCapacityData()
                .Where(w => w.IsActive)
                .ToListAsync(ct);

            if (workCenters.Count == 0)
            {
                run.Status = ScheduleRunStatus.Completed;
                run.CompletedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                return MapToResponse(run);
            }

            var wcMap = workCenters.ToDictionary(w => w.Id);

            var capacityLookup = await WorkCenterCapacityLookup.BuildAsync(db, workCenters, ct);

            // 3. Track capacity usage per work center per date
            var laneFill = new Dictionary<(int WorkCenterId, int Lane, DateOnly Date), decimal>();
            int conflicts = 0;

            // Load existing locked operations (pinned — won't be moved)
            var lockedOps = await db.ScheduledOperations
                .Where(so => so.IsLocked && so.Status != ScheduledOperationStatus.Cancelled)
                .OrderBy(so => so.ScheduledStart)
                .ToListAsync(ct);

            var lockedByJobOperation = lockedOps
                .GroupBy(so => (so.JobId, so.OperationId))
                .ToDictionary(g => g.Key, g => g.Max(so => so.ScheduledEnd));

            foreach (var locked in lockedOps)
            {
                if (!BookLockedOperation(locked, capacityLookup.GetValueOrDefault(locked.WorkCenterId, WorkCenterCapacity.None), laneFill))
                    conflicts++;
            }

            // 4. Remove non-locked scheduled operations (if not simulation)
            if (!isSimulation)
            {
                var toRemove = await db.ScheduledOperations
                    .Where(so => !so.IsLocked && so.Status == ScheduledOperationStatus.Scheduled)
                    .ToListAsync(ct);
                db.ScheduledOperations.RemoveRange(toRemove);
                await db.SaveChangesAsync(ct);
            }

            // 5. Schedule each job's operations
            int scheduledCount = 0;
            var newOps = new List<ScheduledOperation>();

            foreach (var job in jobs)
            {
                var operations = job.Part!.Operations
                    .OrderBy(o => o.StepNumber)
                    .ToList();

                DateTimeOffset? cursor = parameters.Direction == ScheduleDirection.Forward
                    ? null
                    : ScheduleToStart(parameters.ScheduleTo, capacityLookup, operations, workCenters[0].Id);

                DateTimeOffset? previousEnd = null;
                var quantity = OperationTimeMath.JobBuildQuantity(job);

                for (int i = 0; i < operations.Count; i++)
                {
                    var op = operations[i];

                    if (lockedByJobOperation.TryGetValue((job.Id, op.Id), out var lockedEnd))
                    {
                        previousEnd = lockedEnd;
                        cursor = cursor is { } c && c > lockedEnd ? c : lockedEnd;
                        continue;
                    }

                    int wcId = op.WorkCenterId ?? workCenters[0].Id;

                    if (!capacityLookup.ContainsKey(wcId))
                    {
                        wcId = workCenters[0].Id;
                    }

                    var capacity = capacityLookup[wcId];
                    var efficiency = capacity.Efficiency;

                    // Calculate time needed
                    decimal setupMinutes = op.SetupMinutes;
                    decimal runMinutes = OperationTimeMath.PlannedMinutes(op, quantity) - setupMinutes;

                    // Apply scrap factor
                    if (op.ScrapFactor > 0)
                        runMinutes *= (1 + op.ScrapFactor);

                    decimal setupHours = setupMinutes / 60m;
                    decimal runHours = runMinutes / 60m;

                    // Apply efficiency
                    if (efficiency > 0)
                    {
                        setupHours /= efficiency;
                        runHours /= efficiency;
                    }

                    decimal totalHours = setupHours + runHours;

                    // Apply overlap from previous operation
                    if (previousEnd.HasValue && cursor.HasValue && i > 0 && operations[i - 1].OverlapPercent > 0)
                    {
                        decimal overlapFraction = operations[i - 1].OverlapPercent / 100m;
                        var prevDuration = previousEnd.Value - cursor.Value;
                        cursor = cursor.Value.AddHours((double)(-(decimal)prevDuration.TotalHours * overlapFraction));
                    }

                    var notBefore = cursor ?? capacity.MidnightUtc(parameters.ScheduleFrom);
                    var earliest = notBefore > run.RunDate ? notBefore : run.RunDate;
                    var slot = FindAvailableSlot(
                        wcId, earliest, totalHours, capacity, laneFill, parameters.ScheduleTo);

                    if (slot is not (var lane, var start, var end, var allocations))
                    {
                        conflicts++;
                        logger.LogWarning("No capacity found for Job {JobId} Op {OpId} at WorkCenter {WcId}",
                            job.Id, op.Id, wcId);
                        continue;
                    }

                    var schedOp = new ScheduledOperation
                    {
                        JobId = job.Id,
                        OperationId = op.Id,
                        WorkCenterId = wcId,
                        ScheduledStart = start,
                        ScheduledEnd = end,
                        SetupHours = setupHours,
                        RunHours = runHours,
                        TotalHours = totalHours,
                        Status = ScheduledOperationStatus.Scheduled,
                        SequenceNumber = op.StepNumber,
                        ScheduleRunId = run.Id,
                    };

                    newOps.Add(schedOp);
                    scheduledCount++;

                    // Update capacity tracking
                    foreach (var (allocatedDate, filledTo) in allocations)
                    {
                        var laneKey = (wcId, lane, allocatedDate);
                        laneFill[laneKey] = Math.Max(laneFill.GetValueOrDefault(laneKey), filledTo);
                    }

                    previousEnd = end;
                    cursor = end;
                }
            }

            if (!isSimulation)
            {
                db.ScheduledOperations.AddRange(newOps);
            }

            run.Status = ScheduleRunStatus.Completed;
            run.CompletedAt = clock.UtcNow;
            run.OperationsScheduled = scheduledCount;
            run.ConflictsDetected = conflicts;

            db.ScheduleRuns.Update(run);
            await db.SaveChangesAsync(ct);

            return MapToResponse(run);
        }
        catch (Exception ex)
        {
            run.Status = ScheduleRunStatus.Failed;
            run.CompletedAt = clock.UtcNow;
            run.ErrorMessage = ex.Message;
            db.ScheduleRuns.Update(run);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private static DateTimeOffset ScheduleToStart(
        DateOnly scheduleTo, Dictionary<int, WorkCenterCapacity> capacityLookup, List<Operation> operations, int fallbackWorkCenterId)
    {
        var firstWorkCenterId = operations.Select(o => o.WorkCenterId).FirstOrDefault() ?? fallbackWorkCenterId;
        var capacity = capacityLookup.GetValueOrDefault(firstWorkCenterId)
            ?? capacityLookup.GetValueOrDefault(fallbackWorkCenterId)
            ?? WorkCenterCapacity.None;
        return capacity.MidnightUtc(scheduleTo);
    }

    private static (int Lane, DateTimeOffset Start, DateTimeOffset End, List<(DateOnly Date, decimal FilledTo)> Allocations)? FindAvailableSlot(
        int workCenterId,
        DateTimeOffset earliest,
        decimal totalHours,
        WorkCenterCapacity capacity,
        Dictionary<(int, int, DateOnly), decimal> laneFill,
        DateOnly scheduleTo)
    {
        (int Lane, DateTimeOffset Start, DateTimeOffset End, List<(DateOnly Date, decimal FilledTo)> Allocations)? best = null;

        for (var lane = 0; lane < LaneCount(capacity); lane++)
        {
            if (FindSlotOnLane(workCenterId, lane, earliest, totalHours, capacity, laneFill, scheduleTo) is not { } candidate)
                continue;

            if (best is null || candidate.End < best.Value.End)
                best = (lane, candidate.Start, candidate.End, candidate.Allocations);
        }

        return best;
    }

    private static (DateTimeOffset Start, DateTimeOffset End, List<(DateOnly Date, decimal FilledTo)> Allocations)? FindSlotOnLane(
        int workCenterId,
        int lane,
        DateTimeOffset earliest,
        decimal totalHours,
        WorkCenterCapacity capacity,
        Dictionary<(int, int, DateOnly), decimal> laneFill,
        DateOnly scheduleTo)
    {
        var date = capacity.LocalDate(earliest);
        var maxDate = scheduleTo.AddDays(30);
        var allocations = new List<(DateOnly Date, decimal FilledTo)>();
        var hoursLeft = totalHours;
        var start = DateTimeOffset.MinValue;

        while (date <= maxDate)
        {
            decimal availableHours = capacity.HoursOn(date);
            decimal filled = laneFill.GetValueOrDefault((workCenterId, lane, date));
            var dayStart = capacity.DayStartUtc(date);

            if (allocations.Count > 0)
            {
                if (availableHours > 0 && filled > 0)
                {
                    allocations.Clear();
                    hoursLeft = totalHours;
                    earliest = dayStart;
                    continue;
                }

                if (availableHours > 0)
                {
                    var continued = Math.Min(availableHours, hoursLeft);
                    allocations.Add((date, continued));
                    hoursLeft -= continued;

                    if (hoursLeft <= 0)
                        return (start, dayStart.AddHours((double)continued), allocations);
                }

                date = date.AddDays(1);
                continue;
            }

            var offsetHours = Math.Max(filled, (decimal)(earliest - dayStart).TotalHours);
            if (offsetHours < 0)
                offsetHours = 0;

            decimal remainingHours = availableHours - offsetHours;

            if (remainingHours > 0 || (totalHours <= 0 && remainingHours >= 0 && availableHours > 0))
            {
                var dayHours = Math.Min(remainingHours, hoursLeft);
                start = dayStart.AddHours((double)offsetHours);
                allocations.Add((date, offsetHours + dayHours));
                hoursLeft -= dayHours;

                if (hoursLeft <= 0)
                    return (start, dayStart.AddHours((double)(offsetHours + dayHours)), allocations);
            }

            date = date.AddDays(1);
        }

        return null;
    }

    private static bool BookLockedOperation(
        ScheduledOperation op,
        WorkCenterCapacity capacity,
        Dictionary<(int, int, DateOnly), decimal> laneFill)
    {
        var segments = new List<(DateOnly Date, decimal From, decimal To)>();
        var lastDate = capacity.LocalDate(op.ScheduledEnd);
        for (var date = capacity.LocalDate(op.ScheduledStart).AddDays(-1); date <= lastDate; date = date.AddDays(1))
        {
            var dayStart = capacity.DayStartUtc(date);
            var nextDayStart = capacity.DayStartUtc(date.AddDays(1));
            var from = op.ScheduledStart > dayStart ? op.ScheduledStart : dayStart;
            var to = op.ScheduledEnd < nextDayStart ? op.ScheduledEnd : nextDayStart;
            if (to > from)
                segments.Add((date, (decimal)(from - dayStart).TotalHours, (decimal)(to - dayStart).TotalHours));
        }

        var lanes = LaneCount(capacity);
        var lane = Enumerable.Range(0, lanes)
            .Cast<int?>()
            .FirstOrDefault(l => segments.All(s => laneFill.GetValueOrDefault((op.WorkCenterId, l!.Value, s.Date)) <= s.From));
        var isFree = lane is not null;
        lane ??= Enumerable.Range(0, lanes)
            .OrderBy(l => segments.Sum(s => laneFill.GetValueOrDefault((op.WorkCenterId, l, s.Date))))
            .First();

        foreach (var (date, _, to) in segments)
        {
            var key = (op.WorkCenterId, lane.Value, date);
            laneFill[key] = Math.Max(laneFill.GetValueOrDefault(key), to);
        }

        return isFree;
    }

    private static int LaneCount(WorkCenterCapacity capacity) => Math.Max(1, capacity.Machines);

    private static void SpreadAcrossDays(
        ScheduledOperation op,
        WorkCenterCapacity capacity,
        Dictionary<(int, DateOnly), decimal> capacityUsed)
    {
        var date = capacity.LocalDate(op.ScheduledStart);
        var lastDate = capacity.LocalDate(op.ScheduledEnd);
        var hoursLeft = op.TotalHours;

        while (date < lastDate && hoursLeft > 0)
        {
            var dayKey = (op.WorkCenterId, date);
            var dayRemaining = capacity.MachineHoursOn(date) - capacityUsed.GetValueOrDefault(dayKey);
            var dayHours = Math.Min(hoursLeft, Math.Max(0m, dayRemaining));
            if (dayHours > 0)
            {
                capacityUsed[dayKey] = capacityUsed.GetValueOrDefault(dayKey) + dayHours;
                hoursLeft -= dayHours;
            }
            date = date.AddDays(1);
        }

        var key = (op.WorkCenterId, date);
        capacityUsed[key] = capacityUsed.GetValueOrDefault(key) + hoursLeft;
    }

    private static List<Job> SortByPriorityRule(List<Job> jobs, string priorityRule)
    {
        return priorityRule switch
        {
            "DueDate" => jobs.OrderBy(j => j.DueDate ?? DateTimeOffset.MaxValue).ToList(),
            "Priority" => jobs.OrderByDescending(j => j.Priority).ThenBy(j => j.DueDate).ToList(),
            "FIFO" => jobs.OrderBy(j => j.CreatedAt).ToList(),
            _ => jobs.OrderBy(j => j.DueDate ?? DateTimeOffset.MaxValue).ToList(),
        };
    }

    public async Task RescheduleOperationAsync(int scheduledOperationId, DateTimeOffset newStart, CancellationToken ct)
    {
        var op = await db.ScheduledOperations.FindAsync([scheduledOperationId], ct)
            ?? throw new KeyNotFoundException($"Scheduled operation {scheduledOperationId} not found.");

        if (op.IsLocked)
            throw new InvalidOperationException("Cannot reschedule a locked operation.");

        var duration = op.ScheduledEnd - op.ScheduledStart;
        op.ScheduledStart = newStart;
        op.ScheduledEnd = newStart + duration;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkCenterLoadResponseModel> GetWorkCenterLoadAsync(
        int workCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var wc = await db.WorkCenters
            .AsNoTracking()
            .WithCapacityData()
            .FirstOrDefaultAsync(w => w.Id == workCenterId, ct)
            ?? throw new KeyNotFoundException($"Work center {workCenterId} not found.");

        var capacity = (await WorkCenterCapacityLookup.BuildAsync(db, [wc], ct))[wc.Id];

        var rangeStart = capacity.MidnightUtc(from);
        var rangeEnd = capacity.MidnightUtc(to.AddDays(1));
        var scheduledOps = await db.ScheduledOperations
            .AsNoTracking()
            .Where(so => so.WorkCenterId == workCenterId
                && so.Status != ScheduledOperationStatus.Cancelled
                && so.ScheduledStart >= rangeStart
                && so.ScheduledStart < rangeEnd)
            .OrderBy(so => so.ScheduledStart)
            .ToListAsync(ct);

        var dailyLoad = new Dictionary<(int, DateOnly), decimal>();
        foreach (var so in scheduledOps)
            SpreadAcrossDays(so, capacity, dailyLoad);

        // Group by week
        var buckets = new List<WorkCenterLoadBucket>();
        var current = from;

        // Align to Monday
        while (current.DayOfWeek != System.DayOfWeek.Monday)
            current = current.AddDays(-1);

        while (current <= to)
        {
            var weekEnd = current.AddDays(6);
            decimal weekCapacity = 0;
            decimal weekScheduled = 0;

            for (var d = current; d <= weekEnd; d = d.AddDays(1))
            {
                weekCapacity += capacity.MachineHoursOn(d);
            }

            weekScheduled = dailyLoad
                .Where(kv => kv.Key.Item2 >= current && kv.Key.Item2 <= weekEnd)
                .Sum(kv => kv.Value);

            decimal utilization = weekCapacity > 0 ? weekScheduled / weekCapacity * 100m : 0;

            buckets.Add(new WorkCenterLoadBucket(current, weekCapacity, weekScheduled, Math.Round(utilization, 1)));
            current = current.AddDays(7);
        }

        return new WorkCenterLoadResponseModel(wc.Id, wc.Name, buckets);
    }

    public async Task<IReadOnlyList<DispatchListItemModel>> GetDispatchListAsync(int workCenterId, CancellationToken ct)
    {
        var ops = await db.ScheduledOperations
            .AsNoTracking()
            .Include(so => so.Job)
            .Include(so => so.Operation)
            .Where(so => so.WorkCenterId == workCenterId
                && so.Status == ScheduledOperationStatus.Scheduled
                && so.ScheduledStart >= clock.UtcNow.AddDays(-1))
            .OrderBy(so => so.ScheduledStart)
            .Take(50)
            .ToListAsync(ct);

        return ops.Select(so => new DispatchListItemModel(
            so.Id,
            so.JobId,
            so.Job.JobNumber,
            so.OperationId,
            so.Operation.Title,
            so.SequenceNumber,
            so.ScheduledStart,
            so.SetupHours,
            so.RunHours,
            so.Job.Priority.ToString(),
            so.Job.DueDate)).ToList();
    }

    public decimal CalculateAvailableCapacity(
        int workCenterId, DateOnly date,
        IReadOnlyList<WorkCenterShiftInfo> shifts,
        IReadOnlyDictionary<DateOnly, decimal> calendarOverrides)
    {
        return (WorkCenterCapacity.None with { Shifts = shifts, Overrides = calendarOverrides }).HoursOn(date);
    }

    private static ScheduleRunResponseModel MapToResponse(ScheduleRun run)
    {
        return new ScheduleRunResponseModel(
            run.Id, run.RunDate, run.Direction, run.Status,
            run.OperationsScheduled, run.ConflictsDetected,
            run.CompletedAt, run.RunByUserId, run.ErrorMessage);
    }
}
