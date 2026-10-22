using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Costing.Tier3;
using Forge.Api.Features.Quality;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Costing;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Concurrency;

/// <summary>
/// A double-click or a repeated Enter sends the same create twice a moment apart. These handlers
/// check before they insert with no unique index behind the check, so they serialize on a
/// PostgreSQL advisory lock. Each blocking test holds the lock from a competing transaction,
/// commits the competing row while the second request waits, and proves the second request then
/// sees it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DoubleSubmitSerializationTests(PostgresFixture fixture)
{
    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(500);

    private async Task<(AppDbContext Db, IDbContextTransaction Tx)> HoldLockAsync(string scope, string key)
    {
        var db = fixture.CreateContext();
        var tx = await db.Database.BeginTransactionAsync();
        await db.AcquireAdvisoryLockAsync(scope, key, CancellationToken.None);
        return (db, tx);
    }

    private async Task<ApplicationUser> AddUserAsync()
    {
        await using var db = fixture.CreateContext();
        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = $"u{tag}@t.com", Email = $"u{tag}@t.com",
            FirstName = "T", LastName = "U", Initials = "TU", AvatarColor = "#888",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IHttpContextAccessor SignedIn(int userId)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(h => h.HttpContext).Returns(() => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
        });
        return accessor.Object;
    }

    [Fact]
    public async Task StartTimer_second_press_waits_for_the_first_and_then_sees_its_timer()
    {
        var user = await AddUserAsync();
        var (holder, held) = await HoldLockAsync("time-entries.timer", user.Id.ToString());

        await using var db = fixture.CreateContext();
        var hub = new Mock<IHubContext<TimerHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var clock = new SystemClock();
        var handler = new StartTimerHandler(
            new TimeTrackingRepository(db), new JobRepository(db, clock), db, SignedIn(user.Id),
            hub.Object, Mock.Of<IMediator>(), clock);

        var second = handler.Handle(new StartTimerCommand(new StartTimerRequestModel(null, "Setup", null)), CancellationToken.None);
        (await Task.WhenAny(second, Task.Delay(BlockedFor))).Should().NotBe(second);

        holder.TimeEntries.Add(new TimeEntry
        {
            UserId = user.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            TimerStart = DateTimeOffset.UtcNow,
        });
        await holder.SaveChangesAsync();
        await held.CommitAsync();
        await holder.DisposeAsync();

        var act = () => second;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("You're already running a timer*");

        await using var verify = fixture.CreateContext();
        (await verify.TimeEntries.CountAsync(t => t.UserId == user.Id && t.TimerStop == null)).Should().Be(1);
    }

    [Fact]
    public async Task Scheduler_second_run_waits_for_the_first_and_then_refuses()
    {
        await using (var clean = fixture.CreateContext())
            await clean.ScheduleRuns.Where(r => r.Status == ScheduleRunStatus.Running).ExecuteDeleteAsync();

        var (holder, held) = await HoldLockAsync("schedule-runs.running", string.Empty);

        await using var db = fixture.CreateContext();
        var service = new SchedulingService(db, new SystemClock(), NullLogger<SchedulingService>.Instance);
        var from = DateOnly.FromDateTime(DateTime.UtcNow);

        var second = service.ScheduleAsync(
            new ScheduleParameters(ScheduleDirection.Forward, from, from.AddDays(30), null, "DueDate", null), CancellationToken.None);
        (await Task.WhenAny(second, Task.Delay(BlockedFor))).Should().NotBe(second);

        var first = new ScheduleRun { RunDate = DateTimeOffset.UtcNow, Status = ScheduleRunStatus.Running, ParametersJson = "{}" };
        holder.ScheduleRuns.Add(first);
        await holder.SaveChangesAsync();
        await held.CommitAsync();
        await holder.DisposeAsync();

        var act = () => second;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A scheduling run is already in progress.");

        await using var verify = fixture.CreateContext();
        (await verify.ScheduleRuns.CountAsync(r => r.Status == ScheduleRunStatus.Running)).Should().Be(1);
        await verify.ScheduleRuns.Where(r => r.Id == first.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SpcMeasurements_second_entry_waits_and_takes_the_next_subgroup_number()
    {
        var user = await AddUserAsync();
        int characteristicId;
        await using (var seed = fixture.CreateContext())
        {
            var part = new Part { PartNumber = $"SPC-{Guid.NewGuid():N}"[..20], Name = "Shaft" };
            seed.Parts.Add(part);
            await seed.SaveChangesAsync();
            var characteristic = new SpcCharacteristic
            {
                PartId = part.Id, Name = "OD", NominalValue = 10m, UpperSpecLimit = 10.05m, LowerSpecLimit = 9.95m, SampleSize = 2,
            };
            seed.SpcCharacteristics.Add(characteristic);
            await seed.SaveChangesAsync();
            characteristicId = characteristic.Id;
        }

        var (holder, held) = await HoldLockAsync("spc-measurements.subgroup", characteristicId.ToString());

        await using var db = fixture.CreateContext();
        var handler = new RecordSpcMeasurementsHandler(db, Mock.Of<ISpcService>(), SignedIn(user.Id));
        var second = handler.Handle(new RecordSpcMeasurementsCommand(new RecordSpcMeasurementRequestModel
        {
            CharacteristicId = characteristicId,
            Subgroups = [new SpcSubgroupEntry { Values = [10m, 10.01m] }],
        }), CancellationToken.None);
        (await Task.WhenAny(second, Task.Delay(BlockedFor))).Should().NotBe(second);

        holder.SpcMeasurements.Add(new SpcMeasurement
        {
            CharacteristicId = characteristicId, MeasuredById = user.Id, MeasuredAt = DateTimeOffset.UtcNow,
            SubgroupNumber = 1, ValuesJson = "[10,10]",
        });
        await holder.SaveChangesAsync();
        await held.CommitAsync();
        await holder.DisposeAsync();

        (await second).Single().SubgroupNumber.Should().Be(2);
    }

    [Fact]
    public async Task CostCenter_with_a_code_already_in_use_is_refused()
    {
        var code = $"DS{Guid.NewGuid():N}"[..12];
        var command = new CreateCostingCostCenterCommand(code, "Molding", CostCenterType.Production, null, null, null, true);

        await using (var first = fixture.CreateContext())
            await new CreateCostingCostCenterHandler(first).Handle(command, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var act = () => new CreateCostingCostCenterHandler(db).Handle(command with { Code = $" {code} " }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"A cost center with code '{code}' already exists.");
        (await db.CostingCostCenters.CountAsync(c => c.Code == code)).Should().Be(1);
    }

    [Fact]
    public async Task CostCenter_double_submit_creates_one()
    {
        var code = $"DS{Guid.NewGuid():N}"[..12];
        var command = new CreateCostingCostCenterCommand(code, "Molding", CostCenterType.Production, null, null, null, true);
        await using var a = fixture.CreateContext();
        await using var b = fixture.CreateContext();

        var results = await Task.WhenAll(
            Outcome(new CreateCostingCostCenterHandler(a).Handle(command, CancellationToken.None)),
            Outcome(new CreateCostingCostCenterHandler(b).Handle(command, CancellationToken.None)));

        results.Count(ok => ok).Should().Be(1);
        await using var verify = fixture.CreateContext();
        (await verify.CostingCostCenters.CountAsync(c => c.Code == code)).Should().Be(1);
    }

    [Fact]
    public async Task CostingPeriod_with_the_same_dates_is_refused_but_an_overlapping_one_is_not()
    {
        var start = UniqueYearStart();
        var month = new CreateCostingPeriodCommand(start, start.AddMonths(1));

        await using (var first = fixture.CreateContext())
            await new CreateCostingPeriodHandler(first).Handle(month, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var duplicate = () => new CreateCostingPeriodHandler(db).Handle(month, CancellationToken.None);
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("A costing period with these dates already exists.");

        var fiscalYear = await new CreateCostingPeriodHandler(db)
            .Handle(new CreateCostingPeriodCommand(start, start.AddYears(1).AddDays(-1)), CancellationToken.None);
        fiscalYear.StartDate.Should().Be(start);
    }

    [Fact]
    public async Task CostingPeriod_double_submit_creates_one()
    {
        var start = UniqueYearStart();
        var command = new CreateCostingPeriodCommand(start, start.AddMonths(1));
        await using var a = fixture.CreateContext();
        await using var b = fixture.CreateContext();

        var results = await Task.WhenAll(
            Outcome(new CreateCostingPeriodHandler(a).Handle(command, CancellationToken.None)),
            Outcome(new CreateCostingPeriodHandler(b).Handle(command, CancellationToken.None)));

        results.Count(ok => ok).Should().Be(1);
        await using var verify = fixture.CreateContext();
        (await verify.CostingPeriods.CountAsync(p => p.StartDate == start)).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_pool_budgets_from_before_the_fix_freeze_and_edit_the_latest_row()
    {
        int periodId, poolId, olderId, latestId;
        await using (var seed = fixture.CreateContext())
        {
            var start = UniqueYearStart();
            var cc = new CostingCostCenter { Code = $"DB{Guid.NewGuid():N}"[..12], Name = "Mold", Type = CostCenterType.Production, IsInventoriable = true };
            var period = new CostingPeriod { StartDate = start, EndDate = start.AddMonths(1), Status = CostingPeriodStatus.Open };
            seed.CostingCostCenters.Add(cc);
            seed.CostingPeriods.Add(period);
            await seed.SaveChangesAsync();
            var pool = new OverheadCostPool
            {
                CostingCostCenterId = cc.Id, Code = "DUP", Name = "Dup", Behavior = OverheadBehavior.Variable, Driver = OverheadDriver.MachineHour,
            };
            seed.OverheadCostPools.Add(pool);
            await seed.SaveChangesAsync();

            var older = new OverheadPoolBudget { OverheadCostPoolId = pool.Id, CostingPeriodId = period.Id, BudgetAmount = 100m, BudgetDriverQty = 10m };
            seed.OverheadPoolBudgets.Add(older);
            await seed.SaveChangesAsync();
            var latest = new OverheadPoolBudget { OverheadCostPoolId = pool.Id, CostingPeriodId = period.Id, BudgetAmount = 300m, BudgetDriverQty = 10m };
            seed.OverheadPoolBudgets.Add(latest);
            await seed.SaveChangesAsync();

            (periodId, poolId, olderId, latestId) = (period.Id, pool.Id, older.Id, latest.Id);
        }

        await using (var db = fixture.CreateContext())
        {
            var edited = await new UpsertOverheadPoolBudgetHandler(db)
                .Handle(new UpsertOverheadPoolBudgetCommand(poolId, periodId, 500m, 10m), CancellationToken.None);
            edited.Id.Should().Be(latestId);
        }

        await using (var db = fixture.CreateContext())
        {
            var frozen = await new FreezeCostingPeriodHandler(db, new SystemClock())
                .Handle(new FreezeCostingPeriodCommand(periodId), CancellationToken.None);
            frozen.BudgetsRated.Should().Be(1);
        }

        await using var verify = fixture.CreateContext();
        var budgets = await verify.OverheadPoolBudgets.AsNoTracking().Where(b => b.CostingPeriodId == periodId).ToListAsync();
        budgets.Single(b => b.Id == latestId).DerivedRate.Should().Be(50m);
        budgets.Single(b => b.Id == olderId).BudgetAmount.Should().Be(100m);
    }

    [Fact]
    public async Task OverheadPool_double_submit_creates_one_per_cost_center()
    {
        int centerId;
        await using (var seed = fixture.CreateContext())
        {
            var cc = new CostingCostCenter { Code = $"OP{Guid.NewGuid():N}"[..12], Name = "Mold", Type = CostCenterType.Production, IsInventoriable = true };
            seed.CostingCostCenters.Add(cc);
            await seed.SaveChangesAsync();
            centerId = cc.Id;
        }

        var command = new CreateOverheadPoolCommand(centerId, null, "PWR", "Power", OverheadBehavior.Variable, null, OverheadDriver.MachineHour);
        await using var a = fixture.CreateContext();
        await using var b = fixture.CreateContext();

        var results = await Task.WhenAll(
            Outcome(new CreateOverheadPoolHandler(a).Handle(command, CancellationToken.None)),
            Outcome(new CreateOverheadPoolHandler(b).Handle(command with { Code = " PWR " }, CancellationToken.None)));

        results.Count(ok => ok).Should().Be(1);
        await using var verify = fixture.CreateContext();
        (await verify.OverheadCostPools.CountAsync(p => p.CostingCostCenterId == centerId && p.Code == "PWR")).Should().Be(1);
    }

    [Fact]
    public async Task CostCenter_code_that_differs_only_in_case_is_refused()
    {
        var code = $"cc{Guid.NewGuid():N}"[..12];

        await using (var first = fixture.CreateContext())
            await new CreateCostingCostCenterHandler(first).Handle(
                new CreateCostingCostCenterCommand(code, "Molding", CostCenterType.Production, null, null, null, true), CancellationToken.None);

        await using var db = fixture.CreateContext();
        var act = () => new CreateCostingCostCenterHandler(db).Handle(
            new CreateCostingCostCenterCommand(code.ToUpperInvariant(), "Molding", CostCenterType.Production, null, null, null, true), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.CostingCostCenters.CountAsync(c => c.Code.ToUpper() == code.ToUpperInvariant())).Should().Be(1);
    }

    [Fact]
    public async Task CostCenter_double_submit_in_different_case_creates_one()
    {
        var code = $"cc{Guid.NewGuid():N}"[..12];
        var command = new CreateCostingCostCenterCommand(code, "Molding", CostCenterType.Production, null, null, null, true);
        await using var a = fixture.CreateContext();
        await using var b = fixture.CreateContext();

        var results = await Task.WhenAll(
            Outcome(new CreateCostingCostCenterHandler(a).Handle(command, CancellationToken.None)),
            Outcome(new CreateCostingCostCenterHandler(b).Handle(command with { Code = code.ToUpperInvariant() }, CancellationToken.None)));

        results.Count(ok => ok).Should().Be(1);
        await using var verify = fixture.CreateContext();
        (await verify.CostingCostCenters.CountAsync(c => c.Code.ToUpper() == code.ToUpperInvariant())).Should().Be(1);
    }

    [Fact]
    public async Task OverheadPool_code_that_differs_only_in_case_is_refused_on_the_same_cost_center()
    {
        int centerId;
        await using (var seed = fixture.CreateContext())
        {
            var cc = new CostingCostCenter { Code = $"OC{Guid.NewGuid():N}"[..12], Name = "Mold", Type = CostCenterType.Production, IsInventoriable = true };
            seed.CostingCostCenters.Add(cc);
            await seed.SaveChangesAsync();
            centerId = cc.Id;
        }

        var command = new CreateOverheadPoolCommand(centerId, null, "Pwr", "Power", OverheadBehavior.Variable, null, OverheadDriver.MachineHour);
        await using (var first = fixture.CreateContext())
            await new CreateOverheadPoolHandler(first).Handle(command, CancellationToken.None);

        await using var db = fixture.CreateContext();
        var act = () => new CreateOverheadPoolHandler(db).Handle(command with { Code = "PWR" }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.OverheadCostPools.CountAsync(p => p.CostingCostCenterId == centerId)).Should().Be(1);
    }

    private static DateTime UniqueYearStart() =>
        new(3000 + Random.Shared.Next(0, 6000), 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<bool> Outcome<T>(Task<T> work)
    {
        try
        {
            await work;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
