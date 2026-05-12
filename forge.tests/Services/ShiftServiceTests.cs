using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// Shifts effort — capacity + within-shift helper tests.
/// </summary>
public class ShiftServiceTests
{
    private static WorkingCalendar UtcCalendar(int id) => new()
    {
        Id = id,
        Name = "Plant",
        TimeZone = "UTC",
        WorkingDaysMask = 0b0111110, // Mon-Fri
        IsDefault = true,
        IsActive = true,
    };

    [Fact]
    public async Task GetWeeklyCapacityHoursAsync_NoShifts_ReturnsZero()
    {
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.GetWeeklyCapacityHoursAsync(1, CancellationToken.None);
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetWeeklyCapacityHoursAsync_SumsAcrossShiftsAndDays()
    {
        // Shift A: M-F, 8h capacity → 5 × 8 = 40
        // Shift B: Sat OT, 4h capacity → 1 × 4 = 4
        // Total = 44
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = 1, Name = "First",
            DaysOfWeekMask = 0b0111110, // Mon-Fri = 5 bits set
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            CapacityHours = 8m, IsActive = true,
        });
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = 1, Name = "Saturday OT",
            DaysOfWeekMask = 0b1000000, // Saturday only
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0),
            CapacityHours = 4m, IsActive = true,
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.GetWeeklyCapacityHoursAsync(1, CancellationToken.None);
        result.Should().Be(44m);
    }

    [Fact]
    public async Task GetWeeklyCapacityHoursAsync_FallsBackToWallClockWhenCapacityZero()
    {
        // No CapacityHours set, no NetHours → wall-clock from 09:00 to 17:00 = 8h × 5 = 40
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = 1, Name = "First",
            DaysOfWeekMask = 0b0111110,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
            CapacityHours = 0m, NetHours = 0m, IsActive = true,
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.GetWeeklyCapacityHoursAsync(1, CancellationToken.None);
        result.Should().Be(40m);
    }

    [Fact]
    public async Task GetWeeklyCapacityHoursAsync_SkipsLegacyTemplateShifts()
    {
        // A row with WorkingCalendarId=null is a work-center template,
        // not a calendar-bound shift. It must NOT contribute to capacity.
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = null, Name = "Legacy template",
            DaysOfWeekMask = 0b0111110,
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            CapacityHours = 8m, IsActive = true,
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        var result = await svc.GetWeeklyCapacityHoursAsync(1, CancellationToken.None);
        result.Should().Be(0m);
    }

    [Fact]
    public async Task IsWithinShiftAsync_StandardShift_TrueDuringWindow()
    {
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = 1, Name = "First",
            DaysOfWeekMask = 0b0111110, // Mon-Fri
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        // Wednesday 2026-05-06 at 10:00 UTC — inside window.
        var inside = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 6, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);
        inside.Should().BeTrue();
        // Wednesday 17:00 UTC — outside window.
        var after = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 6, 17, 0, 0, TimeSpan.Zero), CancellationToken.None);
        after.Should().BeFalse();
        // Sunday — outside day mask.
        var sun = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero), CancellationToken.None);
        sun.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinShiftAsync_GraveyardShift_HandlesMidnightWrap()
    {
        // Shift starts Friday 22:00 UTC, ends Saturday 06:00 UTC. The
        // shift "owns" Friday (its start day = Friday in mask).
        var db = TestDbContextFactory.Create();
        db.WorkingCalendars.Add(UtcCalendar(1));
        db.Shifts.Add(new Shift
        {
            WorkingCalendarId = 1, Name = "Graveyard",
            DaysOfWeekMask = 0b0100000, // Friday only
            StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(6, 0),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var svc = new ShiftService(db);
        // Friday 23:00 UTC — inside the late portion.
        var fri = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 8, 23, 0, 0, TimeSpan.Zero), CancellationToken.None);
        fri.Should().BeTrue();
        // Saturday 03:00 UTC — inside the wraparound portion (next day).
        var sat = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 9, 3, 0, 0, TimeSpan.Zero), CancellationToken.None);
        sat.Should().BeTrue();
        // Saturday 07:00 UTC — past the wraparound end.
        var satAfter = await svc.IsWithinShiftAsync(1,
            new DateTimeOffset(2026, 5, 9, 7, 0, 0, TimeSpan.Zero), CancellationToken.None);
        satAfter.Should().BeFalse();
    }
}
