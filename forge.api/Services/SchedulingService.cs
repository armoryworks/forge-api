using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

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
                .Where(j => j.DeletedAt == null
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
                .Include(w => w.Shifts)
                    .ThenInclude(ws => ws.Shift)
                .Include(w => w.CalendarOverrides)
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

            // Build capacity lookup: workCenterId → (shifts, calendar overrides)
            var capacityLookup = workCenters.ToDictionary(
                w => w.Id,
                w => (
                    Shifts: w.Shifts.Select(ws => new WorkCenterShiftInfo(ws.Shift.NetHours, ws.DaysOfWeek)).ToList(),
                    Calendar: w.CalendarOverrides.ToDictionary(c => c.Date, c => c.AvailableHours),
                    Efficiency: w.EfficiencyPercent / 100m,
                    Machines: w.NumberOfMachines
                ));

            // 3. Track capacity usage per work center per date
            var capacityUsed = new Dictionary<(int WorkCenterId, DateOnly Date), decimal>();

            // Load existing locked operations (pinned — won't be moved)
            var lockedOps = await db.ScheduledOperations
                .Where(so => so.IsLocked && so.Status != ScheduledOperationStatus.Cancelled)
                .ToListAsync(ct);

            foreach (var locked in lockedOps)
            {
                var date = DateOnly.FromDateTime(locked.ScheduledStart.UtcDateTime);
                var key = (locked.WorkCenterId, date);
                capacityUsed[key] = capacityUsed.GetValueOrDefault(key) + locked.TotalHours;
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
            int conflicts = 0;
            var newOps = new List<ScheduledOperation>();

            foreach (var job in jobs)
            {
                var operations = job.Part!.Operations
                    .OrderBy(o => o.StepNumber)
                    .ToList();

                DateTimeOffset cursor;
                if (parameters.Direction == ScheduleDirection.Forward)
                    cursor = new DateTimeOffset(parameters.ScheduleFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                else
                    cursor = new DateTimeOffset(parameters.ScheduleTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

                DateTimeOffset? previousEnd = null;

                for (int i = 0; i < operations.Count; i++)
                {
                    var op = operations[i];
                    int wcId = op.WorkCenterId ?? workCenters[0].Id;

                    if (!capacityLookup.ContainsKey(wcId))
                    {
                        wcId = workCenters[0].Id;
                    }

                    var (shifts, calendar, efficiency, machines) = capacityLookup[wcId];

                    // Calculate time needed
                    decimal setupMinutes = op.SetupMinutes;
                    decimal runMinutes = op.RunMinutesLot + (op.RunMinutesEach * (job.Part?.Operations.Count > 0 ? 1 : 1));
                    // Use job quantity if available, otherwise estimate
                    decimal quantity = 1m; // Default; real implementations would use job quantity
                    runMinutes = op.RunMinutesLot + (op.RunMinutesEach * quantity);

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
                    if (previousEnd.HasValue && i > 0 && operations[i - 1].OverlapPercent > 0)
                    {
                        decimal overlapFraction = operations[i - 1].OverlapPercent / 100m;
                        var prevDuration = previousEnd.Value - cursor;
                        cursor = cursor.AddHours((double)(-(decimal)prevDuration.TotalHours * overlapFraction));
                    }

                    // Find available slot
                    var (start, end) = FindAvailableSlot(
                        wcId, cursor, totalHours, machines,
                        shifts, calendar, capacityUsed,
                        parameters.Direction, parameters.ScheduleFrom, parameters.ScheduleTo);

                    if (start == DateTimeOffset.MinValue)
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
                    var startDate = DateOnly.FromDateTime(start.UtcDateTime);
                    var capKey = (wcId, startDate);
                    capacityUsed[capKey] = capacityUsed.GetValueOrDefault(capKey) + totalHours;

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

    private static (DateTimeOffset Start, DateTimeOffset End) FindAvailableSlot(
        int workCenterId,
        DateTimeOffset cursor,
        decimal totalHours,
        int machines,
        List<WorkCenterShiftInfo> shifts,
        Dictionary<DateOnly, decimal> calendar,
        Dictionary<(int, DateOnly), decimal> capacityUsed,
        ScheduleDirection direction,
        DateOnly scheduleFrom,
        DateOnly scheduleTo)
    {
        var date = DateOnly.FromDateTime(cursor.UtcDateTime);
        var maxDate = scheduleTo.AddDays(30); // Allow 30 days beyond horizon

        while (date <= maxDate)
        {
            decimal availableHours = GetDayCapacity(workCenterId, date, shifts, calendar) * machines;
            decimal usedHours = capacityUsed.GetValueOrDefault((workCenterId, date));
            decimal remainingHours = availableHours - usedHours;

            if (remainingHours >= totalHours)
            {
                // Slot found on this date
                var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(8))), TimeSpan.Zero);
                var shiftOffset = usedHours;
                var start = dayStart.AddHours((double)shiftOffset);
                var end = start.AddHours((double)totalHours);
                return (start, end);
            }

            date = date.AddDays(1);
        }

        return (DateTimeOffset.MinValue, DateTimeOffset.MinValue);
    }

    private static decimal GetDayCapacity(
        int workCenterId,
        DateOnly date,
        List<WorkCenterShiftInfo> shifts,
        Dictionary<DateOnly, decimal> calendar)
    {
        // Calendar override takes precedence
        if (calendar.TryGetValue(date, out var overrideHours))
            return overrideHours;

        // Sum shift hours for this day of week
        var dayFlag = date.DayOfWeek switch
        {
            System.DayOfWeek.Monday => DaysOfWeek.Monday,
            System.DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
            System.DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
            System.DayOfWeek.Thursday => DaysOfWeek.Thursday,
            System.DayOfWeek.Friday => DaysOfWeek.Friday,
            System.DayOfWeek.Saturday => DaysOfWeek.Saturday,
            System.DayOfWeek.Sunday => DaysOfWeek.Sunday,
            _ => DaysOfWeek.None,
        };

        decimal totalHours = 0;
        foreach (var shift in shifts)
        {
            if (shift.DaysOfWeek.HasFlag(dayFlag))
                totalHours += shift.NetHours;
        }

        return totalHours;
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
            .Include(w => w.Shifts).ThenInclude(ws => ws.Shift)
            .Include(w => w.CalendarOverrides)
            .FirstOrDefaultAsync(w => w.Id == workCenterId, ct)
            ?? throw new KeyNotFoundException($"Work center {workCenterId} not found.");

        var shifts = wc.Shifts.Select(ws => new WorkCenterShiftInfo(ws.Shift.NetHours, ws.DaysOfWeek)).ToList();
        var calendar = wc.CalendarOverrides.ToDictionary(c => c.Date, c => c.AvailableHours);

        var scheduledOps = await db.ScheduledOperations
            .AsNoTracking()
            .Where(so => so.WorkCenterId == workCenterId
                && so.Status != ScheduledOperationStatus.Cancelled
                && so.ScheduledStart >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                && so.ScheduledStart <= new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero))
            .ToListAsync(ct);

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
                weekCapacity += GetDayCapacity(workCenterId, d, shifts, calendar) * wc.NumberOfMachines;
            }

            weekScheduled = scheduledOps
                .Where(so =>
                {
                    var opDate = DateOnly.FromDateTime(so.ScheduledStart.UtcDateTime);
                    return opDate >= current && opDate <= weekEnd;
                })
                .Sum(so => so.TotalHours);

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
        return GetDayCapacity(workCenterId, date, shifts.ToList(),
            calendarOverrides.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static ScheduleRunResponseModel MapToResponse(ScheduleRun run)
    {
        return new ScheduleRunResponseModel(
            run.Id, run.RunDate, run.Direction, run.Status,
            run.OperationsScheduled, run.ConflictsDetected,
            run.CompletedAt, run.RunByUserId, run.ErrorMessage);
    }
}
