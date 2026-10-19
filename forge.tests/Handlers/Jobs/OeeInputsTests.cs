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

public class OeeInputsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 14);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();

    public OeeInputsTests() => _clock.Setup(c => c.UtcNow).Returns(Now);

    [Fact]
    public async Task RunCreatedOnAJobAndCompletedDirectly_CountsTowardItsWorkCentersOee()
    {
        var (job, part, press) = await SeedJobAtPressAsync();

        var created = await new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 2000, null, null), CancellationToken.None);
        await new UpdateProductionRunHandler(_db, _clock.Object)
            .Handle(new UpdateProductionRunCommand(job.Id, created.Id, 1940, 60, "Completed", null, null, 100m), CancellationToken.None);

        (await _db.ProductionRuns.AsNoTracking().SingleAsync()).WorkCenterId.Should().Be(press.Id);
        var oee = await new OeeService(_db).CalculateOeeAsync(press.Id, Day, Day, CancellationToken.None);
        oee.TotalQuantity.Should().Be(2000m);
        oee.Quality.Should().Be(0.97m);
    }

    [Fact]
    public async Task ExplicitWorkCenter_WinsOverTheJobsOperation()
    {
        var (job, part, _) = await SeedJobAtPressAsync();
        var spare = new WorkCenter { Name = "Spare press", Code = "SP" };
        _db.WorkCenters.Add(spare);
        await _db.SaveChangesAsync();

        await new CreateProductionRunHandler(_db, _clock.Object)
            .Handle(new CreateProductionRunCommand(job.Id, part.Id, 10, null, null, spare.Id), CancellationToken.None);

        (await _db.ProductionRuns.AsNoTracking().SingleAsync()).WorkCenterId.Should().Be(spare.Id);
    }

    [Fact]
    public async Task Performance_UsesTheRunsIdealCycleTimePerUnitBeforeTheWorkCenters()
    {
        var (job, part, press) = await SeedJobAtPressAsync();
        press.IdealCycleTimeSeconds = 60m;
        _db.ProductionRuns.AddRange(
            Run(job, part, press, completed: 100, runMinutes: 100m, idealSeconds: 30m),
            Run(job, part, press, completed: 50, runMinutes: 50m, idealSeconds: null),
            Run(job, part, press, completed: 500, runMinutes: null, idealSeconds: 30m));
        await _db.SaveChangesAsync();

        var oee = await new OeeService(_db).CalculateOeeAsync(press.Id, Day, Day, CancellationToken.None);

        oee.Performance.Should().Be(Math.Round((100m * 30m / 60m + 50m * 60m / 60m) / 150m, 4));
        oee.TotalQuantity.Should().Be(650m);
    }

    [Fact]
    public async Task ScheduledTime_FollowsTheCalendarsWorkingDays()
    {
        _db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", IsDefault = true });
        var press = new WorkCenter { Name = "Press", Code = "PR", DailyCapacityHours = 8m };
        _db.WorkCenters.Add(press);
        await _db.SaveChangesAsync();

        var oee = await new OeeService(_db).CalculateOeeAsync(
            press.Id, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 18), CancellationToken.None);

        oee.ScheduledMinutes.Should().Be(480m);
    }

    [Fact]
    public async Task OeeDays_AreTheCalendarsLocalDays()
    {
        _db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", IsDefault = true, TimeZone = "America/Denver" });
        var (job, part, press) = await SeedJobAtPressAsync();
        var evening = Run(job, part, press, completed: 10, runMinutes: 10m, idealSeconds: null);
        evening.StartedAt = new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);
        _db.ProductionRuns.Add(evening);
        await _db.SaveChangesAsync();

        var local14 = await new OeeService(_db).CalculateOeeAsync(press.Id, Day, Day, CancellationToken.None);
        var local15 = await new OeeService(_db).CalculateOeeAsync(press.Id, Day.AddDays(1), Day.AddDays(1), CancellationToken.None);

        local14.TotalQuantity.Should().Be(10m);
        local15.TotalQuantity.Should().Be(0m);
    }

    private static ProductionRun Run(Job job, Part part, WorkCenter press, int completed, decimal? runMinutes, decimal? idealSeconds) => new()
    {
        JobId = job.Id,
        PartId = part.Id,
        WorkCenterId = press.Id,
        RunNumber = $"RUN-{Guid.NewGuid():N}"[..12],
        TargetQuantity = completed,
        CompletedQuantity = completed,
        Status = ProductionRunStatus.Completed,
        RunTimeMinutes = runMinutes,
        IdealCycleTimeSeconds = idealSeconds,
        StartedAt = new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero),
    };

    private async Task<(Job Job, Part Part, WorkCenter Press)> SeedJobAtPressAsync()
    {
        var press = new WorkCenter { Name = "Press", Code = "PR", DailyCapacityHours = 8m };
        var part = new Part { PartNumber = "CAP-1", Name = "Cap" };
        _db.AddRange(press, part);
        await _db.SaveChangesAsync();
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Mold", WorkCenterId = press.Id });
        var job = new Job { JobNumber = "J-OEE", Title = "Caps", PartId = part.Id, TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return (job, part, press);
    }
}
