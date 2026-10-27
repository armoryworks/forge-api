using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Scheduling;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scheduling;

public class ShiftWorkCenterTests
{
    private const int Weekdays = (int)(DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private int AddWorkCenter(string name)
    {
        var wc = new WorkCenter { Name = name, Code = name[..3].ToUpperInvariant() };
        _db.WorkCenters.Add(wc);
        _db.SaveChanges();
        return wc.Id;
    }

    [Fact]
    public async Task A_new_shift_runs_on_the_chosen_work_centers_and_days()
    {
        var mill = AddWorkCenter("Mill");

        var result = await new CreateShiftHandler(_db).Handle(new CreateShiftCommand(
            "Day", new TimeOnly(7, 0), new TimeOnly(15, 30), 30, [new ShiftWorkCenterModel(mill, Weekdays)]), CancellationToken.None);

        result.WorkCenters.Should().ContainSingle()
            .Which.Should().Be(new ShiftWorkCenterModel(mill, Weekdays, "Mill"));
        (await _db.ActivityLogs.AnyAsync(a => a.EntityType == "Shift" && a.EntityId == result.Id && a.Action == "created")).Should().BeTrue();
    }

    [Fact]
    public async Task Updating_replaces_the_work_centers_and_can_deactivate()
    {
        var mill = AddWorkCenter("Mill");
        var lathe = AddWorkCenter("Lathe");
        var created = await new CreateShiftHandler(_db).Handle(new CreateShiftCommand(
            "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, [new ShiftWorkCenterModel(mill, Weekdays)]), CancellationToken.None);

        var updated = await new UpdateShiftHandler(_db).Handle(new UpdateShiftCommand(
            created.Id, "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, false,
            [new ShiftWorkCenterModel(lathe, (int)DaysOfWeek.Saturday)]), CancellationToken.None);

        updated.IsActive.Should().BeFalse();
        updated.WorkCenters!.Select(w => (w.WorkCenterId, w.DaysOfWeek)).Should().Equal((lathe, (int)DaysOfWeek.Saturday));
        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "Shift" && a.Action == "updated");
        log.Description.Should().Contain("work centers");
    }

    [Fact]
    public async Task Leaving_work_centers_out_of_an_update_keeps_them()
    {
        var mill = AddWorkCenter("Mill");
        var created = await new CreateShiftHandler(_db).Handle(new CreateShiftCommand(
            "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, [new ShiftWorkCenterModel(mill, Weekdays)]), CancellationToken.None);

        var updated = await new UpdateShiftHandler(_db).Handle(new UpdateShiftCommand(
            created.Id, "Early", new TimeOnly(6, 0), new TimeOnly(14, 0), 30, true), CancellationToken.None);

        updated.WorkCenters.Should().ContainSingle(w => w.WorkCenterId == mill);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void A_work_center_needs_at_least_one_valid_day(int days)
    {
        new CreateShiftValidator().Validate(new CreateShiftCommand(
            "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, [new ShiftWorkCenterModel(1, days)])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_work_center_is_listed_once()
    {
        new UpdateShiftValidator().Validate(new UpdateShiftCommand(
            1, "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, true,
            [new ShiftWorkCenterModel(1, Weekdays), new ShiftWorkCenterModel(1, 1)])).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_work_center_is_refused()
    {
        var create = () => new CreateShiftHandler(_db).Handle(new CreateShiftCommand(
            "Day", new TimeOnly(7, 0), new TimeOnly(15, 0), 30, [new ShiftWorkCenterModel(424242, Weekdays)]), CancellationToken.None);

        await create.Should().ThrowAsync<KeyNotFoundException>();
    }
}
