using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class SchedulingServiceTests
{
    private static readonly DateOnly From = new(2026, 10, 5);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly SchedulingService _service;
    private int _jobSeq;

    public SchedulingServiceTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 5, 7, 0, 0, TimeSpan.Zero));
        _service = new SchedulingService(_db, clock.Object, NullLogger<SchedulingService>.Instance);
    }

    private static ScheduleParameters Parameters() =>
        new(ScheduleDirection.Forward, From, From.AddDays(14), null, "DueDate", null);

    private async Task<WorkCenter> SeedWorkCenterAsync()
    {
        var shift = new Shift { Name = "Day", NetHours = 8m };
        var wc = new WorkCenter { Name = "Press", Code = "PR" };
        wc.Shifts.Add(new WorkCenterShift
        {
            Shift = shift,
            DaysOfWeek = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday
                | DaysOfWeek.Thursday | DaysOfWeek.Friday,
        });
        _db.WorkCenters.Add(wc);
        await _db.SaveChangesAsync();
        return wc;
    }

    private async Task<WorkCenter> SeedWorkCenterWithoutShiftsAsync(Action<WorkCenter>? configure = null)
    {
        var wc = new WorkCenter { Name = "Mill", Code = "ML" };
        configure?.Invoke(wc);
        _db.WorkCenters.Add(wc);
        await _db.SaveChangesAsync();
        return wc;
    }

    private async Task<(Part Part, Operation Op)> SeedPartAsync(WorkCenter wc, long estimatedMs, decimal runMinutesEach = 0m)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var op = new Operation
        {
            PartId = part.Id,
            StepNumber = 10,
            Title = "Mold",
            WorkCenterId = wc.Id,
            EstimatedMs = estimatedMs,
            RunMinutesEach = runMinutesEach,
        };
        _db.Operations.Add(op);
        await _db.SaveChangesAsync();
        return (part, op);
    }

    private async Task<Job> SeedJobAsync(Part part, decimal? quantity, Action<Job>? configure = null)
    {
        var job = new Job
        {
            JobNumber = $"J-{++_jobSeq}",
            Title = "Job",
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = part.Id,
        };
        if (quantity.HasValue)
            job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = quantity.Value });
        configure?.Invoke(job);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Schedule_SkipsArchivedCompletedAndDisposedJobs()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        var open = await SeedJobAsync(part, 10m);
        var archived = await SeedJobAsync(part, 10m, j => j.IsArchived = true);
        var completed = await SeedJobAsync(part, 10m, j => j.CompletedDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var disposed = await SeedJobAsync(part, 10m, j => j.Disposition = JobDisposition.EnteredInError);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        result.OperationsScheduled.Should().Be(1);
        var scheduledJobIds = await _db.ScheduledOperations.Select(so => so.JobId).ToListAsync();
        scheduledJobIds.Should().ContainSingle().Which.Should().Be(open.Id);
        scheduledJobIds.Should().NotContain([archived.Id, completed.Id, disposed.Id]);
    }

    [Fact]
    public async Task Schedule_ClosedJobInFilter_IsStillSkipped()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        var archived = await SeedJobAsync(part, 10m, j => j.IsArchived = true);

        var parameters = Parameters() with { JobIdFilter = [archived.Id] };
        var result = await _service.ScheduleAsync(parameters, CancellationToken.None);

        result.OperationsScheduled.Should().Be(0);
        (await _db.ScheduledOperations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Schedule_UsesJobQuantityAndEstimatedMs()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 30000);
        await SeedJobAsync(part, 500m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        (scheduled.RunHours * 60m).Should().BeApproximately(250m, 0.0001m);
        scheduled.SetupHours.Should().Be(0m);
        scheduled.TotalHours.Should().Be(scheduled.RunHours);
    }

    [Fact]
    public async Task Schedule_RunMinutesEachTakesPrecedenceOverEstimatedMs()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 30000, runMinutesEach: 0.25m);
        await SeedJobAsync(part, 400m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        (scheduled.RunHours * 60m).Should().BeApproximately(100m, 0.0001m);
    }

    [Fact]
    public async Task Schedule_JobWithoutJobParts_BooksOnePiece()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 30000);
        await SeedJobAsync(part, quantity: null);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        (scheduled.RunHours * 60m).Should().BeApproximately(0.5m, 0.0001m);
    }

    [Fact]
    public async Task Schedule_NineOperationRoutingInSeconds_LoadsTheWorkCenter()
    {
        var wc = await SeedWorkCenterAsync();
        var part = new Part { PartNumber = "BRK-200", Name = "Housing" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        for (var step = 1; step <= 9; step++)
        {
            _db.Operations.Add(new Operation
            {
                PartId = part.Id,
                StepNumber = step * 10,
                Title = $"Op {step}",
                WorkCenterId = wc.Id,
                EstimatedMs = 5000 + step * 5000,
            });
        }
        await _db.SaveChangesAsync();
        await SeedJobAsync(part, 500m);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);
        var load = await _service.GetWorkCenterLoadAsync(wc.Id, From, From.AddDays(20), CancellationToken.None);

        result.OperationsScheduled.Should().Be(9);
        result.ConflictsDetected.Should().Be(0);
        load.Buckets.Sum(b => b.ScheduledHours).Should().BeApproximately(37.5m, 0.0001m);
    }

    [Fact]
    public async Task Schedule_OperationLongerThanAShift_SpansIntoTheNextDay()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        var longJob = await SeedJobAsync(part, 500m, j => j.DueDate = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));
        var nextJob = await SeedJobAsync(part, 60m, j => j.DueDate = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        result.OperationsScheduled.Should().Be(2);
        result.ConflictsDetected.Should().Be(0);
        var longOp = await _db.ScheduledOperations.SingleAsync(so => so.JobId == longJob.Id);
        longOp.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        longOp.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 6, 8, 20, 0, TimeSpan.Zero));
        var nextOp = await _db.ScheduledOperations.SingleAsync(so => so.JobId == nextJob.Id);
        nextOp.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 6, 8, 20, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_MultiDayOperation_SkipsNonWorkingDaysAndSpreadsLoad()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 2880m);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);
        var load = await _service.GetWorkCenterLoadAsync(wc.Id, From, From.AddDays(13), CancellationToken.None);

        result.ConflictsDetected.Should().Be(0);
        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 12, 16, 0, 0, TimeSpan.Zero));
        load.Buckets.Select(b => b.ScheduledHours).Should().Equal(40m, 8m);
    }

    [Fact]
    public async Task Schedule_OperationLargerThanTheOverflowWindow_IsAConflict()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 18000m);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        result.OperationsScheduled.Should().Be(0);
        result.ConflictsDetected.Should().Be(1);
        (await _db.ScheduledOperations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Schedule_LockedMultiDayOperation_HoldsCapacityOnEveryDayItSpans()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, op) = await SeedPartAsync(wc, estimatedMs: 60000);
        var lockedJob = await SeedJobAsync(part, 960m, j => j.IsArchived = true);
        _db.ScheduledOperations.Add(new ScheduledOperation
        {
            JobId = lockedJob.Id,
            OperationId = op.Id,
            WorkCenterId = wc.Id,
            ScheduledStart = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero),
            ScheduledEnd = new DateTimeOffset(2026, 10, 6, 16, 0, 0, TimeSpan.Zero),
            RunHours = 16m,
            TotalHours = 16m,
            Status = ScheduledOperationStatus.Scheduled,
            IsLocked = true,
        });
        await _db.SaveChangesAsync();
        var openJob = await SeedJobAsync(part, 60m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync(so => so.JobId == openJob.Id);
        scheduled.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_WorkCenterWithoutShifts_UsesDailyCapacityOnWeekdays()
    {
        var wc = await SeedWorkCenterWithoutShiftsAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 30000);
        await SeedJobAsync(part, 500m);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);
        var load = await _service.GetWorkCenterLoadAsync(wc.Id, From, From.AddDays(13), CancellationToken.None);

        result.OperationsScheduled.Should().Be(1);
        result.ConflictsDetected.Should().Be(0);
        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 12, 10, 0, TimeSpan.Zero));
        load.Buckets[0].CapacityHours.Should().Be(40m);
        load.Buckets.Sum(b => b.ScheduledHours).Should().BeApproximately(250m / 60m, 0.0001m);
    }

    [Fact]
    public async Task Schedule_WorkCenterWithoutShifts_SkipsWeekendsAndDefaultCalendarHolidays()
    {
        var calendar = new WorkingCalendar { Name = "Plant", IsDefault = true };
        calendar.Holidays.Add(new Holiday { Name = "Shutdown", Date = new DateOnly(2026, 10, 12) });
        _db.WorkingCalendars.Add(calendar);
        var wc = await SeedWorkCenterWithoutShiftsAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 2880m);

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        result.ConflictsDetected.Should().Be(0);
        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_WorkCenterWithoutShifts_FollowsItsLocationCalendar()
    {
        var sixDay = new WorkingCalendar { Name = "Six day", WorkingDaysMask = 126 };
        var location = new CompanyLocation { Name = "North", WorkingCalendar = sixDay };
        _db.CompanyLocations.Add(location);
        await _db.SaveChangesAsync();
        var wc = await SeedWorkCenterWithoutShiftsAsync(w => w.CompanyLocationId = location.Id);
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 2880m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 10, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_WorkCenterWithoutShifts_CalendarOverrideWins()
    {
        var wc = await SeedWorkCenterWithoutShiftsAsync(w =>
        {
            w.CalendarOverrides.Add(new WorkCenterCalendar { Date = From, AvailableHours = 0m });
            w.CalendarOverrides.Add(new WorkCenterCalendar { Date = new DateOnly(2026, 10, 10), AvailableHours = 8m });
        });
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 2400m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero));
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 10, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_StartsOnTheCalendarsFirstShiftInItsTimeZone()
    {
        SeedDenverCalendar();
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 60m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_FollowsDaylightSavingTimeOnTheLocalShiftStart()
    {
        SeedDenverCalendar();
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 60m);
        var from = new DateOnly(2026, 11, 2);

        await _service.ScheduleAsync(Parameters() with { ScheduleFrom = from, ScheduleTo = from.AddDays(14) }, CancellationToken.None);

        (await _db.ScheduledOperations.SingleAsync()).ScheduledStart
            .Should().Be(new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_WorkCenterShiftStartBeatsTheCalendarShift()
    {
        SeedDenverCalendar();
        var wc = new WorkCenter { Name = "Late press", Code = "LP" };
        wc.Shifts.Add(new WorkCenterShift
        {
            Shift = new Shift { Name = "Second", StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(22, 30), NetHours = 8m },
            DaysOfWeek = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday,
        });
        _db.WorkCenters.Add(wc);
        await _db.SaveChangesAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 60m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        (await _db.ScheduledOperations.SingleAsync()).ScheduledStart
            .Should().Be(new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_SuccessorNeverStartsBeforeItsPredecessorEnds()
    {
        var mill = await SeedWorkCenterAsync();
        var lathe = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(mill, estimatedMs: 60000);
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 20, Title = "Turn", WorkCenterId = lathe.Id, EstimatedMs = 60000 });
        await _db.SaveChangesAsync();
        await SeedJobAsync(part, 180m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var ops = await _db.ScheduledOperations.OrderBy(so => so.SequenceNumber).ToListAsync();
        ops.Should().HaveCount(2);
        ops[0].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 11, 0, 0, TimeSpan.Zero));
        ops[1].ScheduledStart.Should().Be(ops[0].ScheduledEnd);
        ops[1].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_SuccessorPastTheEndOfTheShift_MovesToTheNextDay()
    {
        var mill = await SeedWorkCenterAsync();
        var lathe = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(mill, estimatedMs: 60000);
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 20, Title = "Turn", WorkCenterId = lathe.Id, EstimatedMs = 60000 });
        await _db.SaveChangesAsync();
        await SeedJobAsync(part, 450m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        var ops = await _db.ScheduledOperations.OrderBy(so => so.SequenceNumber).ToListAsync();
        ops[0].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero));
        ops[1].ScheduledStart.Should().Be(ops[0].ScheduledEnd);
        ops[1].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 6, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_LockedOperation_IsNotScheduledAgain()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, op) = await SeedPartAsync(wc, estimatedMs: 60000);
        var job = await SeedJobAsync(part, 60m);
        _db.ScheduledOperations.Add(new ScheduledOperation
        {
            JobId = job.Id,
            OperationId = op.Id,
            WorkCenterId = wc.Id,
            ScheduledStart = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero),
            ScheduledEnd = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero),
            RunHours = 1m,
            TotalHours = 1m,
            Status = ScheduledOperationStatus.Scheduled,
            IsLocked = true,
        });
        await _db.SaveChangesAsync();

        var result = await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        result.OperationsScheduled.Should().Be(0);
        (await _db.ScheduledOperations.CountAsync(so => so.JobId == job.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Schedule_WorkCenterWithShifts_SkipsCalendarHolidays()
    {
        var calendar = new WorkingCalendar { Name = "Plant", IsDefault = true };
        calendar.Holidays.Add(new Holiday { Name = "Shutdown", Date = From });
        _db.WorkingCalendars.Add(calendar);
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 60m);

        await _service.ScheduleAsync(Parameters(), CancellationToken.None);

        (await _db.ScheduledOperations.SingleAsync()).ScheduledStart
            .Should().Be(new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_RunMidShift_NeverStartsBeforeTheRun()
    {
        SeedDenverCalendar();
        var wc = await SeedWorkCenterAsync();
        var (part, _) = await SeedPartAsync(wc, estimatedMs: 60000);
        await SeedJobAsync(part, 60m);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 5, 14, 30, 0, TimeSpan.Zero));
        var service = new SchedulingService(_db, clock.Object, NullLogger<SchedulingService>.Instance);

        await service.ScheduleAsync(Parameters(), CancellationToken.None);

        var scheduled = await _db.ScheduledOperations.SingleAsync();
        scheduled.ScheduledStart.Should().Be(new DateTimeOffset(2026, 10, 5, 14, 30, 0, TimeSpan.Zero));
        scheduled.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Schedule_SuccessorOfAFinishedLockedOperation_StartsNoEarlierThanTheRun()
    {
        var wc = await SeedWorkCenterAsync();
        var (part, first) = await SeedPartAsync(wc, estimatedMs: 60000);
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 20, Title = "Trim", WorkCenterId = wc.Id, EstimatedMs = 60000 });
        await _db.SaveChangesAsync();
        var job = await SeedJobAsync(part, 60m);
        _db.ScheduledOperations.Add(new ScheduledOperation
        {
            JobId = job.Id,
            OperationId = first.Id,
            WorkCenterId = wc.Id,
            ScheduledStart = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero),
            ScheduledEnd = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
            RunHours = 1m,
            TotalHours = 1m,
            Status = ScheduledOperationStatus.Scheduled,
            IsLocked = true,
        });
        await _db.SaveChangesAsync();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 5, 10, 15, 0, TimeSpan.Zero));
        var service = new SchedulingService(_db, clock.Object, NullLogger<SchedulingService>.Instance);

        await service.ScheduleAsync(Parameters(), CancellationToken.None);

        (await _db.ScheduledOperations.SingleAsync(so => so.JobId == job.Id && !so.IsLocked)).ScheduledStart
            .Should().Be(new DateTimeOffset(2026, 10, 5, 10, 15, 0, TimeSpan.Zero));
    }

    private void SeedDenverCalendar()
    {
        var calendar = new WorkingCalendar { Name = "Plant", IsDefault = true, TimeZone = "America/Denver" };
        calendar.Shifts.Add(new Shift { Name = "Night", StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0), NetHours = 8m });
        calendar.Shifts.Add(new Shift { Name = "First", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 30), NetHours = 8m });
        _db.WorkingCalendars.Add(calendar);
    }
}
