using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task CreateWorkCenter_WithTeam_RecordsOwnerAndLogsCreation()
    {
        using var db = TestDbContextFactory.Create();
        var team = new Team { Name = "Machining" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var result = await new CreateWorkCenterHandler(db).Handle(new CreateWorkCenterCommand(
            "CNC Mill", "WC-CNC-02", null, 8m, 100m, 1, 0m, 0m, null, null, 0, team.Id), CancellationToken.None);

        result.TeamId.Should().Be(team.Id);
        result.TeamName.Should().Be("Machining");
        var log = await db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WorkCenter" && a.Action == "created");
        log.EntityId.Should().Be(result.Id);
        log.Description.Should().Be("Created work center WC-CNC-02 owned by Machining");
    }

    [Fact]
    public async Task CreateWorkCenter_WithUnknownTeam_IsRejected()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new CreateWorkCenterHandler(db).Handle(new CreateWorkCenterCommand(
            "CNC Mill", "WC-CNC-03", null, 8m, 100m, 1, 0m, 0m, null, null, 0, 9999), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "teamId");
        (await db.WorkCenters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateWorkCenter_ChangingTeam_LogsOneRollupRow()
    {
        using var db = TestDbContextFactory.Create();
        var team = new Team { Name = "Finishing" };
        var wc = new WorkCenter { Name = "Deburr", Code = "WC-DB", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 };
        db.AddRange(team, wc);
        await db.SaveChangesAsync();

        var result = await new UpdateWorkCenterHandler(db).Handle(new UpdateWorkCenterCommand(
            wc.Id, "Deburr bench", "WC-DB", null, 8m, 100m, 1, 0m, 0m, true, null, null, 0, team.Id), CancellationToken.None);

        result.TeamId.Should().Be(team.Id);
        result.TeamName.Should().Be("Finishing");
        var log = await db.ActivityLogs.AsNoTracking().SingleAsync(a => a.EntityType == "WorkCenter" && a.Action == "updated");
        log.Description.Should().Be("Updated work center WC-DB — 2 fields: name, teamId");
    }

    [Fact]
    public async Task UpdateWorkCenter_KeepingAnInactiveTeam_IsAllowed()
    {
        using var db = TestDbContextFactory.Create();
        var team = new Team { Name = "Retired", IsActive = false };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var wc = new WorkCenter { Name = "Lathe", Code = "WC-LA", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1, TeamId = team.Id };
        db.WorkCenters.Add(wc);
        await db.SaveChangesAsync();

        var result = await new UpdateWorkCenterHandler(db).Handle(new UpdateWorkCenterCommand(
            wc.Id, "Lathe", "WC-LA", null, 10m, 100m, 1, 0m, 0m, true, null, null, 0, team.Id), CancellationToken.None);

        result.TeamId.Should().Be(team.Id);
        result.DailyCapacityHours.Should().Be(10m);
    }

    [Fact]
    public async Task UpdateWorkCenter_MovingToAnInactiveTeam_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        var team = new Team { Name = "Retired", IsActive = false };
        var wc = new WorkCenter { Name = "Lathe", Code = "WC-LB", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 };
        db.AddRange(team, wc);
        await db.SaveChangesAsync();

        var act = () => new UpdateWorkCenterHandler(db).Handle(new UpdateWorkCenterCommand(
            wc.Id, "Lathe", "WC-LB", null, 8m, 100m, 1, 0m, 0m, true, null, null, 0, team.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetWorkCenters_CarriesTheOwningTeam()
    {
        using var db = TestDbContextFactory.Create();
        var team = new Team { Name = "Assembly" };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        db.WorkCenters.AddRange(
            new WorkCenter { Name = "Bench", Code = "WC-BE", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1, TeamId = team.Id },
            new WorkCenter { Name = "Press", Code = "WC-PR", DailyCapacityHours = 8m, EfficiencyPercent = 100m, NumberOfMachines = 1 });
        await db.SaveChangesAsync();

        var result = await new GetWorkCentersHandler(db).Handle(new GetWorkCentersQuery(), CancellationToken.None);

        result.Single(w => w.Code == "WC-BE").TeamName.Should().Be("Assembly");
        result.Single(w => w.Code == "WC-PR").TeamId.Should().BeNull();
    }
}
