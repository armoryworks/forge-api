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

/// <summary>
/// S10-17#holiday: a forward run from a date later than today (here the week of Thanksgiving, with the plant
/// closed 11-26 and 11-27) must not start, end or book work on a holiday.
/// </summary>
public class SchedulingHolidayFromLaterStartTests
{
    private static readonly DateOnly From = new(2026, 11, 23);
    private static readonly DateOnly[] Holidays = [new(2026, 11, 26), new(2026, 11, 27)];

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly SchedulingService _service;
    private int _jobSeq;

    public SchedulingHolidayFromLaterStartTests()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero));
        _service = new SchedulingService(_db, clock.Object, NullLogger<SchedulingService>.Instance);
    }

    private static ScheduleParameters Forward() =>
        new(ScheduleDirection.Forward, From, From.AddDays(30), null, "DueDate", null);

    private void SeedCalendar(string? timeZone = null, bool withShift = false)
    {
        var calendar = new WorkingCalendar { Name = "Plant", IsDefault = true, TimeZone = timeZone ?? "UTC" };
        if (withShift)
            calendar.Shifts.Add(new Shift { Name = "First", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 30), NetHours = 8m });
        calendar.Holidays.Add(new Holiday { Name = "Thanksgiving", Date = Holidays[0] });
        calendar.Holidays.Add(new Holiday { Name = "Day after Thanksgiving", Date = Holidays[1] });
        _db.WorkingCalendars.Add(calendar);
    }

    private async Task<WorkCenter> SeedWorkCenterAsync(bool withShifts)
    {
        var wc = new WorkCenter { Name = "Press", Code = "PR" };
        if (withShifts)
        {
            wc.Shifts.Add(new WorkCenterShift
            {
                Shift = new Shift { Name = "Day", NetHours = 8m },
                DaysOfWeek = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday
                    | DaysOfWeek.Thursday | DaysOfWeek.Friday,
            });
        }
        _db.WorkCenters.Add(wc);
        await _db.SaveChangesAsync();
        return wc;
    }

    private async Task<Part> SeedRoutingAsync(WorkCenter wc, params int[] stepMinutes)
    {
        var part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        for (var i = 0; i < stepMinutes.Length; i++)
        {
            _db.Operations.Add(new Operation
            {
                PartId = part.Id, StepNumber = (i + 1) * 10, Title = $"Step {i + 1}", WorkCenterId = wc.Id,
                SetupMinutes = stepMinutes[i],
            });
        }
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Job> SeedJobAsync(Part part)
    {
        var job = new Job { JobNumber = $"J-{++_jobSeq}", Title = "Job", TrackTypeId = 1, CurrentStageId = 1, PartId = part.Id };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 1m });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<List<ScheduledOperation>> ScheduledAsync() =>
        await _db.ScheduledOperations.OrderBy(so => so.SequenceNumber).ToListAsync();

    private static void ShouldAvoidHolidays(IEnumerable<ScheduledOperation> ops, TimeZoneInfo zone)
    {
        foreach (var op in ops)
        {
            var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(op.ScheduledStart, zone).DateTime);
            var end = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(op.ScheduledEnd, zone).DateTime);
            Holidays.Should().NotContain(start, $"step {op.SequenceNumber} starts {op.ScheduledStart:u}");
            Holidays.Should().NotContain(end, $"step {op.SequenceNumber} ends {op.ScheduledEnd:u}");
            start.Should().BeOnOrAfter(From);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forward_from_thanksgiving_week_resumes_the_routing_on_the_monday_after(bool workCenterShifts)
    {
        SeedCalendar();
        var wc = await SeedWorkCenterAsync(workCenterShifts);
        var part = await SeedRoutingAsync(wc, 960, 480, 240);
        await SeedJobAsync(part);

        var result = await _service.ScheduleAsync(Forward(), CancellationToken.None);

        result.ConflictsDetected.Should().Be(0);
        var ops = await ScheduledAsync();
        ops.Should().HaveCount(3);
        ShouldAvoidHolidays(ops, TimeZoneInfo.Utc);
        ops[0].ScheduledStart.Should().Be(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        ops[1].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero));
        ops[2].ScheduledStart.Should().Be(new DateTimeOffset(2026, 11, 30, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_multi_day_operation_in_thanksgiving_week_books_nothing_on_the_holidays()
    {
        SeedCalendar();
        var wc = await SeedWorkCenterAsync(withShifts: true);
        var part = await SeedRoutingAsync(wc, 1920);
        await SeedJobAsync(part);

        await _service.ScheduleAsync(Forward(), CancellationToken.None);

        var op = (await ScheduledAsync()).Single();
        ShouldAvoidHolidays([op], TimeZoneInfo.Utc);
        op.ScheduledEnd.Should().Be(new DateTimeOffset(2026, 11, 30, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_local_time_zone_calendar_keeps_the_holidays_on_local_dates()
    {
        SeedCalendar("America/Denver", withShift: true);
        var wc = await SeedWorkCenterAsync(withShifts: false);
        var part = await SeedRoutingAsync(wc, 960, 480, 240);
        await SeedJobAsync(part);

        await _service.ScheduleAsync(Forward(), CancellationToken.None);

        var ops = await ScheduledAsync();
        ops.Should().HaveCount(3);
        ShouldAvoidHolidays(ops, TimeZoneInfo.FindSystemTimeZoneById("America/Denver"));
        ops[2].ScheduledStart.Should().Be(new DateTimeOffset(2026, 11, 30, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_second_job_queued_behind_the_first_also_skips_the_holidays()
    {
        SeedCalendar();
        var wc = await SeedWorkCenterAsync(withShifts: true);
        var part = await SeedRoutingAsync(wc, 1200);
        await SeedJobAsync(part);
        await SeedJobAsync(part);

        await _service.ScheduleAsync(Forward(), CancellationToken.None);

        var ops = await ScheduledAsync();
        ops.Should().HaveCount(2);
        ShouldAvoidHolidays(ops, TimeZoneInfo.Utc);
        ops.Max(o => o.ScheduledEnd).Should().Be(new DateTimeOffset(2026, 12, 1, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_routing_whose_first_step_is_locked_weeks_earlier_still_runs_across_thanksgiving_from_the_run_start()
    {
        SeedCalendar();
        var wc = await SeedWorkCenterAsync(withShifts: true);
        var part = await SeedRoutingAsync(wc, 60, 1440, 240);
        var job = await SeedJobAsync(part);
        var first = await _db.Operations.SingleAsync(o => o.PartId == part.Id && o.StepNumber == 10);
        _db.ScheduledOperations.Add(new ScheduledOperation
        {
            JobId = job.Id, OperationId = first.Id, WorkCenterId = wc.Id,
            ScheduledStart = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero),
            ScheduledEnd = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero),
            RunHours = 1m, TotalHours = 1m, Status = ScheduledOperationStatus.Scheduled, IsLocked = true, SequenceNumber = 10,
        });
        await _db.SaveChangesAsync();

        await _service.ScheduleAsync(Forward(), CancellationToken.None);

        var placed = (await ScheduledAsync()).Where(so => !so.IsLocked).ToList();
        placed.Should().HaveCount(2);
        ShouldAvoidHolidays(placed, TimeZoneInfo.Utc);
        placed[0].ScheduledStart.Should().Be(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        placed[0].ScheduledEnd.Should().Be(new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero));
        placed[1].ScheduledStart.Should().Be(new DateTimeOffset(2026, 11, 30, 8, 0, 0, TimeSpan.Zero));
    }
}
