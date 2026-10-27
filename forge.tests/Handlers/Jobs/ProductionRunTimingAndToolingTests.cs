using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class ProductionRunTimingAndToolingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 14);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();

    public ProductionRunTimingAndToolingTests() => _clock.Setup(c => c.UtcNow).Returns(Now);

    [Theory]
    [InlineData(2000, 0, 4, 500)]
    [InlineData(1999, 0, 4, 500)]
    [InlineData(10, 3, null, 13)]
    [InlineData(0, 0, 4, 0)]
    public void ShotsFor_rounds_pieces_up_to_whole_shots(int good, int scrap, int? cavities, int shots)
    {
        ProductionRunTooling.ShotsFor(good, scrap, cavities).Should().Be(shots);
    }

    [Fact]
    public async Task Completed_run_time_subtracts_downtime_logged_in_the_window_so_oee_performance_is_positive()
    {
        var (job, part, press) = await SeedAsync();
        press.IdealCycleTimeSeconds = 18m;
        var machine = new Asset { Name = "Press 1", AssetType = AssetType.Machine, WorkCenterId = press.Id };
        _db.Assets.Add(machine);
        await _db.SaveChangesAsync();
        _db.DowntimeLogs.AddRange(
            new DowntimeLog { AssetId = machine.Id, WorkCenterId = press.Id, StartedAt = Now.AddMinutes(-50), EndedAt = Now.AddMinutes(-40), Reason = "Jam" },
            new DowntimeLog { AssetId = machine.Id, WorkCenterId = press.Id, StartedAt = Now.AddMinutes(-45), EndedAt = Now.AddMinutes(-35), Reason = "Jam again" },
            new DowntimeLog { AssetId = machine.Id, WorkCenterId = press.Id, StartedAt = Now.AddHours(-5), EndedAt = Now.AddHours(-4), Reason = "Before the run" });
        await _db.SaveChangesAsync();

        var run = await StartAsync(job, part, Now.AddHours(-1));
        var completed = await Update(job, run, 100, 0, "Completed", setup: 10m, runTime: null);

        completed.RunTimeMinutes.Should().Be(35m, "60 elapsed − 10 setup − 15 downtime (overlaps counted once)");
        completed.RunTimeManual.Should().BeFalse();
        var oee = await new OeeService(_db).CalculateOeeAsync(press.Id, Day, Day, CancellationToken.None);
        oee.Performance.Should().BeGreaterThan(0m);
        oee.Performance.Should().Be(Math.Round(Math.Min(1m, 100m * 18m / 60m / 35m), 4));
    }

    [Fact]
    public async Task Entered_run_time_wins_and_survives_a_later_save_without_one()
    {
        var (job, part, _) = await SeedAsync();
        var run = await StartAsync(job, part, Now.AddHours(-1));

        var manual = await Update(job, run, 100, 0, "Completed", setup: null, runTime: 42m);
        manual.RunTimeMinutes.Should().Be(42m);
        manual.RunTimeManual.Should().BeTrue();

        var resaved = await Update(job, run, 98, 2, "Completed", setup: null, runTime: null);
        resaved.RunTimeMinutes.Should().Be(42m);

        var cleared = await Update(job, run, 98, 2, "Completed", setup: null, runTime: 0m);
        cleared.RunTimeManual.Should().BeFalse();
        cleared.RunTimeMinutes.Should().Be(60m, "clearing the entered value goes back to the derived run time");
    }

    [Fact]
    public async Task Run_without_timestamps_takes_its_run_time_from_run_labour_at_its_work_center()
    {
        var (job, part, press) = await SeedAsync();
        var other = new WorkCenter { Name = "Trim", Code = "TR" };
        _db.WorkCenters.Add(other);
        await _db.SaveChangesAsync();
        _db.TimeEntries.AddRange(
            new TimeEntry { JobId = job.Id, UserId = 1, Date = Day, DurationMinutes = 90, EntryType = TimeEntryType.Run, WorkCenterId = press.Id },
            new TimeEntry { JobId = job.Id, UserId = 1, Date = Day, DurationMinutes = 30, EntryType = TimeEntryType.Setup, WorkCenterId = press.Id },
            new TimeEntry { JobId = job.Id, UserId = 1, Date = Day, DurationMinutes = 45, EntryType = TimeEntryType.Run, WorkCenterId = other.Id });
        await _db.SaveChangesAsync();
        var created = await new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 100, null, null, press.Id), CancellationToken.None);

        var completed = await new UpdateProductionRunHandler(_db, _clock.Object)
            .Handle(new UpdateProductionRunCommand(job.Id, created.Id, 100, 0, "Completed", null, null, null), CancellationToken.None);

        completed.RunTimeMinutes.Should().Be(90m);
    }

    [Fact]
    public async Task Moving_a_run_to_another_tool_and_deleting_it_take_its_shots_back()
    {
        var (job, part, _) = await SeedAsync();
        var moldA = new Asset { Name = "Mold A", AssetType = AssetType.Tooling, CavityCount = 2, CurrentShotCount = 1000 };
        var moldB = new Asset { Name = "Mold B", AssetType = AssetType.Tooling, CavityCount = 4 };
        _db.Assets.AddRange(moldA, moldB);
        await _db.SaveChangesAsync();
        var created = await new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 100, null, null, null, moldA.Id), CancellationToken.None);

        await new UpdateProductionRunHandler(_db, _clock.Object)
            .Handle(new UpdateProductionRunCommand(job.Id, created.Id, 100, 0, "InProgress", null, null, null), CancellationToken.None);
        moldA.CurrentShotCount.Should().Be(1050);

        var moved = await new UpdateProductionRunHandler(_db, _clock.Object)
            .Handle(new UpdateProductionRunCommand(job.Id, created.Id, 100, 0, "InProgress", null, null, null, moldB.Id), CancellationToken.None);
        moved.ToolingAssetName.Should().Be("Mold B");
        moldA.CurrentShotCount.Should().Be(1000);
        moldB.CurrentShotCount.Should().Be(25);

        await new DeleteProductionRunHandler(_db, _clock.Object)
            .Handle(new DeleteProductionRunCommand(job.Id, created.Id), CancellationToken.None);
        moldB.CurrentShotCount.Should().Be(0);
    }

    [Fact]
    public async Task A_machine_cannot_be_chosen_as_a_runs_tooling()
    {
        var (job, part, _) = await SeedAsync();
        var machine = new Asset { Name = "Press 1", AssetType = AssetType.Machine };
        _db.Assets.Add(machine);
        await _db.SaveChangesAsync();

        var act = () => new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 10, null, null, null, machine.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<int> StartAsync(Job job, Part part, DateTimeOffset startedAt)
    {
        var created = await new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 100, null, null), CancellationToken.None);
        _clock.Setup(c => c.UtcNow).Returns(startedAt);
        await Update(job, created.Id, 0, 0, "InProgress", null, null);
        _clock.Setup(c => c.UtcNow).Returns(Now);
        return created.Id;
    }

    private Task<Forge.Core.Models.ProductionRunResponseModel> Update(
        Job job, int runId, int good, int scrap, string status, decimal? setup, decimal? runTime) =>
        new UpdateProductionRunHandler(_db, _clock.Object)
            .Handle(new UpdateProductionRunCommand(job.Id, runId, good, scrap, status, null, setup, runTime), CancellationToken.None);

    private async Task<(Job Job, Part Part, WorkCenter Press)> SeedAsync()
    {
        var press = new WorkCenter { Name = "Press", Code = "PR", DailyCapacityHours = 8m };
        var part = new Part { PartNumber = "CAP-1", Name = "Cap" };
        _db.AddRange(press, part);
        await _db.SaveChangesAsync();
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Mold", WorkCenterId = press.Id });
        var job = new Job { JobNumber = "J-RT", Title = "Caps", PartId = part.Id, TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return (job, part, press);
    }
}
