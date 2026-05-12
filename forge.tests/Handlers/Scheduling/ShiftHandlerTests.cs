using FluentAssertions;

using Forge.Api.Features.Scheduling;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scheduling;

public class ShiftHandlerTests
{
    [Fact]
    public async Task CreateShift_CalculatesNetHours()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new CreateShiftHandler(db);

        var result = await handler.Handle(new CreateShiftCommand(
            "Day Shift", new TimeOnly(7, 0), new TimeOnly(15, 30), 30), CancellationToken.None);

        result.Should().NotBeNull();
        result.Name.Should().Be("Day Shift");
        result.NetHours.Should().Be(8m); // 8.5 hours - 30 min break = 8 hours
    }

    [Fact]
    public async Task UpdateShift_RecalculatesNetHours()
    {
        using var db = TestDbContextFactory.Create();
        var shift = new Shift { Name = "Old Shift", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), BreakMinutes = 30, NetHours = 7.5m };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        var handler = new UpdateShiftHandler(db);
        var result = await handler.Handle(new UpdateShiftCommand(
            shift.Id, "Updated Shift", new TimeOnly(6, 0), new TimeOnly(14, 0), 60, true), CancellationToken.None);

        result.Name.Should().Be("Updated Shift");
        result.NetHours.Should().Be(7m); // 8 hours - 60 min break = 7 hours
    }

    [Fact]
    public async Task DeleteShift_SoftDeletes()
    {
        using var db = TestDbContextFactory.Create();
        var shift = new Shift { Name = "Delete Shift", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), BreakMinutes = 0, NetHours = 8m };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        var handler = new DeleteShiftHandler(db);
        await handler.Handle(new DeleteShiftCommand(shift.Id), CancellationToken.None);

        var updated = await db.Shifts.FindAsync(shift.Id);
        updated!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetShifts_ReturnsAll()
    {
        using var db = TestDbContextFactory.Create();
        db.Shifts.AddRange(
            new Shift { Name = "Day", StartTime = new TimeOnly(7, 0), EndTime = new TimeOnly(15, 0), BreakMinutes = 30, NetHours = 7.5m },
            new Shift { Name = "Night", StartTime = new TimeOnly(23, 0), EndTime = new TimeOnly(7, 0), BreakMinutes = 30, NetHours = 7.5m }
        );
        await db.SaveChangesAsync();

        var handler = new GetShiftsHandler(db);
        var result = await handler.Handle(new GetShiftsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
    }
}
