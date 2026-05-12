using FluentAssertions;

using Forge.Api.Features.Scheduling;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Scheduling;

public class WorkCenterHandlerTests
{
    [Fact]
    public async Task CreateWorkCenter_ReturnsNewWorkCenter()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new CreateWorkCenterHandler(db);

        var result = await handler.Handle(new CreateWorkCenterCommand(
            "CNC Mill", "WC-CNC-01", "3-axis CNC milling center",
            8m, 85m, 2, 45m, 25m, null, null, 1), CancellationToken.None);

        result.Should().NotBeNull();
        result.Name.Should().Be("CNC Mill");
        result.Code.Should().Be("WC-CNC-01");
        result.DailyCapacityHours.Should().Be(8m);
        result.EfficiencyPercent.Should().Be(85m);
        result.NumberOfMachines.Should().Be(2);
    }

    [Fact]
    public async Task UpdateWorkCenter_UpdatesFields()
    {
        using var db = TestDbContextFactory.Create();
        var wc = new WorkCenter { Name = "Old Name", Code = "WC-01", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 };
        db.WorkCenters.Add(wc);
        await db.SaveChangesAsync();

        var handler = new UpdateWorkCenterHandler(db);
        var result = await handler.Handle(new UpdateWorkCenterCommand(
            wc.Id, "New Name", "WC-02", "Updated", 10m, 90m, 3, 50m, 30m, true, null, null, 2), CancellationToken.None);

        result.Name.Should().Be("New Name");
        result.Code.Should().Be("WC-02");
        result.DailyCapacityHours.Should().Be(10m);
        result.NumberOfMachines.Should().Be(3);
    }

    [Fact]
    public async Task DeleteWorkCenter_SoftDeletes()
    {
        using var db = TestDbContextFactory.Create();
        var wc = new WorkCenter { Name = "Delete Me", Code = "WC-DEL", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 };
        db.WorkCenters.Add(wc);
        await db.SaveChangesAsync();

        var handler = new DeleteWorkCenterHandler(db);
        await handler.Handle(new DeleteWorkCenterCommand(wc.Id), CancellationToken.None);

        var updated = await db.WorkCenters.FindAsync(wc.Id);
        updated!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWorkCenters_ReturnsAll()
    {
        using var db = TestDbContextFactory.Create();
        db.WorkCenters.AddRange(
            new WorkCenter { Name = "WC A", Code = "WC-A", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 },
            new WorkCenter { Name = "WC B", Code = "WC-B", DailyCapacityHours = 16m, EfficiencyPercent = 90m, NumberOfMachines = 2 }
        );
        await db.SaveChangesAsync();

        var handler = new GetWorkCentersHandler(db);
        var result = await handler.Handle(new GetWorkCentersQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
    }
}
