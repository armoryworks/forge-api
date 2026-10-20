using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Scheduling;
using Forge.Api.Features.WorkingCalendars;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.WorkingCalendars;

public class CalendarShiftHoursTests
{
    private readonly Forge.Data.Context.AppDbContext _db = TestDbContextFactory.Create();

    private async Task<WorkingCalendar> SeedCalendarAsync()
    {
        var calendar = new WorkingCalendar { Name = "Plant" };
        _db.WorkingCalendars.Add(calendar);
        await _db.SaveChangesAsync();
        return calendar;
    }

    [Fact]
    public async Task AddCalendarShift_StoresNetHoursAcrossMidnight()
    {
        var calendar = await SeedCalendarAsync();

        var result = await new AddCalendarShiftHandler(_db).Handle(
            new AddCalendarShiftCommand(calendar.Id, new CalendarShiftRequestModel(
                "Night", 62, new TimeOnly(22, 0), new TimeOnly(6, 30), 1m, 0m, true)),
            CancellationToken.None);

        (await _db.Shifts.SingleAsync()).NetHours.Should().Be(8.5m);
        result.EffectiveCapacityHours.Should().Be(8.5m);
        var listed = await new GetShiftsHandler(_db).Handle(new GetShiftsQuery(), CancellationToken.None);
        listed.Single().NetHours.Should().Be(8.5m);
    }

    [Fact]
    public async Task GetShifts_CalendarShiftSavedWithoutNetHours_ShowsItsSpan()
    {
        var calendar = await SeedCalendarAsync();
        _db.Shifts.Add(new Shift { Name = "Day", WorkingCalendarId = calendar.Id, StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0) });
        await _db.SaveChangesAsync();

        var listed = await new GetShiftsHandler(_db).Handle(new GetShiftsQuery(), CancellationToken.None);

        listed.Single().NetHours.Should().Be(8m);
    }

    [Fact]
    public async Task UpdateCalendarShift_RecomputesNetHours()
    {
        var calendar = await SeedCalendarAsync();
        var shift = new Shift { Name = "Day", WorkingCalendarId = calendar.Id, StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 0), NetHours = 8m };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();

        await new UpdateCalendarShiftHandler(_db).Handle(
            new UpdateCalendarShiftCommand(calendar.Id, shift.Id, new CalendarShiftRequestModel(
                "Day", 62, new TimeOnly(6, 0), new TimeOnly(16, 0), 1m, 0m, true)),
            CancellationToken.None);

        (await _db.Shifts.SingleAsync()).NetHours.Should().Be(10m);
    }

    [Fact]
    public async Task CapacityLookup_ShiftWithoutCapacityOrNetHours_FallsBackToTheSpan()
    {
        var wc = new WorkCenter { Name = "Press", Code = "PR" };
        wc.Shifts.Add(new WorkCenterShift
        {
            Shift = new Shift { Name = "Day", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(14, 30) },
            DaysOfWeek = DaysOfWeek.Monday,
        });
        _db.WorkCenters.Add(wc);
        await _db.SaveChangesAsync();
        var loaded = await _db.WorkCenters.WithCapacityData().ToListAsync();

        var capacity = (await WorkCenterCapacityLookup.BuildAsync(_db, loaded, CancellationToken.None))[wc.Id];

        capacity.HoursOn(new DateOnly(2026, 10, 5)).Should().Be(8.5m);
    }
}
