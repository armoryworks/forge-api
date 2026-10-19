using FluentAssertions;

using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class OvertimeServiceWorkedHoursTests
{
    private static readonly DateOnly LastMonday = new(2026, 10, 12);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public OvertimeServiceWorkedHoursTests()
    {
        _db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = "America/Denver", IsDefault = true });
        _db.SaveChanges();
    }

    private OvertimeService Service() => new(_db, TestCaller.WorkedHours(_db, Now));

    [Fact]
    public async Task Manual_entries_count_toward_overtime()
    {
        for (var i = 0; i < 5; i++)
            _db.TimeEntries.Add(new TimeEntry { UserId = 7, Date = LastMonday.AddDays(i), DurationMinutes = 540, IsManual = true });
        await _db.SaveChangesAsync();

        var ot = await Service().CalculateOvertimeAsync(7, LastMonday, LastMonday.AddDays(6), CancellationToken.None);

        ot.RegularHours.Should().Be(40m);
        ot.OvertimeHours.Should().Be(5m);
    }

    [Fact]
    public async Task An_evening_timer_counts_on_its_shop_day_not_the_utc_date()
    {
        var sundayEvening = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        _db.TimeEntries.Add(new TimeEntry
        {
            UserId = 7, Date = new DateOnly(2026, 10, 18), TimerStart = sundayEvening,
            TimerStop = sundayEvening.AddHours(2), DurationMinutes = 120,
        });
        await _db.SaveChangesAsync();

        var ot = await Service().CalculateOvertimeAsync(7, LastMonday, LastMonday.AddDays(6), CancellationToken.None);

        ot.RegularHours.Should().Be(2m);
        ot.DailyBreakdown.Single(d => d.Date == new DateOnly(2026, 10, 18)).TotalHours.Should().Be(2m);
    }

    [Fact]
    public async Task Clocked_attendance_counts_when_it_exceeds_the_logged_time()
    {
        var tuesdayStart = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
        _db.ClockEvents.AddRange(
            new ClockEvent { UserId = 7, EventTypeCode = "ClockIn", Timestamp = tuesdayStart },
            new ClockEvent { UserId = 7, EventTypeCode = "BreakStart", Timestamp = tuesdayStart.AddHours(5) },
            new ClockEvent { UserId = 7, EventTypeCode = "BreakEnd", Timestamp = tuesdayStart.AddHours(5.5) },
            new ClockEvent { UserId = 7, EventTypeCode = "ClockOut", Timestamp = tuesdayStart.AddHours(10.5) });
        _db.TimeEntries.Add(new TimeEntry { UserId = 7, Date = new DateOnly(2026, 10, 13), DurationMinutes = 120 });
        await _db.SaveChangesAsync();

        var ot = await Service().CalculateOvertimeAsync(7, LastMonday, LastMonday.AddDays(6), CancellationToken.None);

        ot.RegularHours.Should().Be(8m);
        ot.OvertimeHours.Should().Be(2m, "ten clocked hours, the break excluded");
    }

    [Fact]
    public async Task A_forgotten_clock_out_is_capped_rather_than_running_all_week()
    {
        _db.ClockEvents.Add(new ClockEvent { UserId = 7, EventTypeCode = "ClockIn", Timestamp = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero) });
        await _db.SaveChangesAsync();

        var hours = await TestCaller.WorkedHours(_db, Now)
            .DailyHoursAsync([7], LastMonday, LastMonday.AddDays(6), CancellationToken.None);

        hours[7].Values.Sum().Should().Be(16m);
    }
}
