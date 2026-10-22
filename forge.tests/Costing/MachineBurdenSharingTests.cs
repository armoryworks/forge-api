using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

public class MachineBurdenSharingTests
{
    private const decimal Rate = 30m;
    private const decimal Burden = 60m;
    private readonly TimerTestHarness _h = new();

    private async Task<(Job Job, JobOperation Step, Operation Operation)> SeedStepAsync(BurdenBasis basis = BurdenBasis.Machine)
    {
        var mill = new WorkCenter { Name = "Mill", Code = $"WC-{basis}", LaborCostPerHour = Rate, BurdenRatePerHour = Burden, BurdenBasis = basis, IsActive = true };
        _h.Db.Add(mill);
        await _h.Db.SaveChangesAsync();
        var operation = new Operation { PartId = 1, StepNumber = 20, Title = "Mill", WorkCenterId = mill.Id };
        _h.Db.Add(operation);
        var job = await _h.AddJobAsync($"J-{basis}");
        var step = new JobOperation { JobId = job.Id, OperationId = operation.Id, StepNumber = 20, Title = "Mill", Status = JobOperationStatus.InProgress };
        _h.Db.Add(step);
        await _h.Db.SaveChangesAsync();
        return (job, step, operation);
    }

    private async Task<TimeEntry> AddStoppedAsync(
        int userId, JobOperation step, double startMinutesAgo, double stopMinutesAgo,
        LaborAllocationMethod? method = LaborAllocationMethod.Weighted, bool locked = false)
    {
        var start = _h.Now.AddMinutes(-startMinutesAgo);
        var stop = _h.Now.AddMinutes(-stopMinutesAgo);
        var entry = new TimeEntry
        {
            UserId = userId,
            JobId = step.JobId,
            OperationId = step.OperationId,
            JobOperationId = step.Id,
            EntryType = TimeEntryType.Run,
            Date = DateOnly.FromDateTime(start.UtcDateTime),
            TimerStart = start,
            TimerStop = stop,
            DurationMinutes = (int)(stop - start).TotalMinutes,
            AllocationMethod = method,
            IsLocked = locked,
        };
        _h.Db.TimeEntries.Add(entry);
        await _h.Db.SaveChangesAsync();
        return entry;
    }

    private Task<TimeEntry> StoredAsync(int id) => _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == id);

    [Fact]
    public async Task Two_operators_on_one_machine_step_burden_the_cycle_once()
    {
        var (_, step, _) = await SeedStepAsync();
        var pat = await _h.AddUserAsync();
        var sam = await _h.AddUserAsync();
        var a = await AddStoppedAsync(pat.Id, step, 60, 0);
        var b = await AddStoppedAsync(sam.Id, step, 60, 0);
        var cost = new JobCostService(_h.Db);

        await cost.PriceTimeEntryAsync(a, default);
        await cost.PriceTimeEntryAsync(b, default);

        a.BurdenCost.Should().Be(30m);
        b.BurdenCost.Should().Be(30m);
        a.LaborCost.Should().Be(30m, "labor is not shared across people, only machine time");
    }

    [Fact]
    public async Task Staggered_operators_burden_the_union_of_their_time()
    {
        var (_, step, _) = await SeedStepAsync();
        var a = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 90, 30);
        var b = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 60, 0);
        var cost = new JobCostService(_h.Db);

        await cost.PriceTimeEntryAsync(a, default);
        await cost.PriceTimeEntryAsync(b, default);

        (a.BurdenCost + b.BurdenCost).Should().BeApproximately(90m, 0.0001m, "the machine ran 90 minutes at 60 an hour");
        a.BurdenCost.Should().BeApproximately(45m, 0.0001m);
    }

    [Fact]
    public async Task A_labor_basis_step_is_not_shared()
    {
        var (_, step, _) = await SeedStepAsync(BurdenBasis.Labor);
        var a = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 60, 0);
        var b = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 60, 0);
        var cost = new JobCostService(_h.Db);

        await cost.PriceTimeEntryAsync(a, default);
        await cost.PriceTimeEntryAsync(b, default);

        a.BurdenCost.Should().Be(60m);
        b.BurdenCost.Should().Be(60m);
    }

    [Fact]
    public async Task Stopping_one_operator_while_the_other_still_runs_writes_a_share_that_stays_put()
    {
        var (job, step, operation) = await SeedStepAsync();
        var pat = await _h.AddUserAsync();
        var sam = await _h.AddUserAsync();
        await _h.AddRunningTimerAsync(pat.Id, job.Id, _h.Now.AddMinutes(-60), operation.Id, step.Id);
        await _h.AddRunningTimerAsync(sam.Id, job.Id, _h.Now.AddMinutes(-30), operation.Id, step.Id);

        await _h.StopActiveTimerHandler().Handle(
            new StopActiveTimerCommand(pat.Id, _h.Now, Reason: "clocked out", IncludeOperationTimers: true), default);
        var first = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.UserId == pat.Id);
        first.BurdenCost.Should().BeApproximately(45m, 0.0001m, "30 minutes alone, then 30 shared with a running timer");

        _h.Now = _h.Now.AddMinutes(30);
        await _h.StopActiveTimerHandler().Handle(
            new StopActiveTimerCommand(sam.Id, _h.Now, Reason: "clocked out", IncludeOperationTimers: true), default);

        var second = await _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.UserId == sam.Id);
        (await StoredAsync(first.Id)).BurdenCost.Should().Be(first.BurdenCost, "a written share is not re-priced on a later stop");
        (first.BurdenCost + second.BurdenCost).Should().BeApproximately(90m, 0.0001m, "the machine ran 90 minutes once");
    }

    [Fact]
    public async Task Editing_a_timer_re_prices_the_machine_share_of_the_other_operators()
    {
        var (_, step, _) = await SeedStepAsync();
        var pat = await _h.AddUserAsync();
        var sam = await _h.AddUserAsync();
        var a = await AddStoppedAsync(pat.Id, step, 120, 60);
        var b = await AddStoppedAsync(sam.Id, step, 120, 60);
        var cost = new JobCostService(_h.Db);
        await cost.PriceTimeEntryAsync(a, default);
        await cost.PriceTimeEntryAsync(b, default);
        await _h.Db.SaveChangesAsync();
        a.BurdenCost.Should().Be(30m);
        var laborBefore = a.LaborCost;

        var before = (b.TimerStart!.Value, b.TimerStop!.Value);
        b.TimerStart = _h.Now.AddMinutes(-60);
        b.TimerStop = _h.Now;
        await _h.LaborAllocation().ReallocateAsync(sam.Id, [before, (b.TimerStart.Value, b.TimerStop.Value)], [b], default);
        await _h.Db.SaveChangesAsync();

        var stored = await StoredAsync(a.Id);
        stored.BurdenCost.Should().Be(60m, "the other operator no longer overlaps, so this timer carries the whole hour");
        stored.LaborCost.Should().Be(laborBefore, "a sibling's labor is not re-priced");
    }

    [Fact]
    public async Task Locked_and_pre_upgrade_siblings_keep_their_cost_on_an_edit()
    {
        var (_, step, _) = await SeedStepAsync();
        var locked = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 120, 60, locked: true);
        var legacy = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 120, 60, method: null);
        locked.BurdenCost = 11m;
        legacy.BurdenCost = 22m;
        var edited = await AddStoppedAsync((await _h.AddUserAsync()).Id, step, 120, 60);

        edited.TimerStart = _h.Now.AddMinutes(-30);
        await new JobCostService(_h.Db).RepriceMachineSiblingsAsync([edited], default);
        await _h.Db.SaveChangesAsync();

        (await StoredAsync(locked.Id)).BurdenCost.Should().Be(11m);
        (await StoredAsync(legacy.Id)).BurdenCost.Should().Be(22m);
    }
}
