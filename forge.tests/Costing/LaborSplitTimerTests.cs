using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TimeTracking;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

public class LaborSplitTimerTests
{
    private const decimal Rate = 30m;
    private const decimal Burden = 12m;
    private readonly TimerTestHarness _h = new();

    private async Task<(Operation Machine, Operation Filler1, Operation Filler2, WorkCenter MachineCenter)> SeedRoutingAsync(
        BurdenBasis machineBasis = BurdenBasis.Machine)
    {
        var lathe = new WorkCenter { Name = "Lathe", Code = "WC-LATHE", LaborCostPerHour = Rate, BurdenRatePerHour = Burden, BurdenBasis = machineBasis, IsActive = true };
        var bench = new WorkCenter { Name = "Bench", Code = "WC-BENCH", LaborCostPerHour = Rate, BurdenRatePerHour = Burden, IsActive = true };
        _h.Db.AddRange(lathe, bench);
        await _h.Db.SaveChangesAsync();
        var machine = new Operation { PartId = 1, StepNumber = 2, Title = "Turn", WorkCenterId = lathe.Id, LaborAttendancePercent = 20m };
        var filler1 = new Operation { PartId = 2, StepNumber = 8, Title = "Deburr", WorkCenterId = bench.Id };
        var filler2 = new Operation { PartId = 3, StepNumber = 4, Title = "Inspect", WorkCenterId = bench.Id };
        _h.Db.AddRange(machine, filler1, filler2);
        await _h.Db.SaveChangesAsync();
        return (machine, filler1, filler2, lathe);
    }

    private Task ClockOutAsync(int userId)
        => _h.StopActiveTimerHandler().Handle(
            new StopActiveTimerCommand(userId, _h.Now, Reason: "clocked out", IncludeOperationTimers: true), CancellationToken.None);

    private Task<TimeEntry> StoredAsync(int id) => _h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == id);

    [Fact]
    public async Task Clock_out_with_three_overlapping_steps_charges_the_clocked_time_once_split_by_attendance()
    {
        var user = await _h.AddUserAsync();
        var (machine, filler1, filler2, _) = await SeedRoutingAsync();
        var jobA = await _h.AddJobAsync("J-A");
        var jobB = await _h.AddJobAsync("J-B");
        var jobC = await _h.AddJobAsync("J-C");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-60));
        var a = await _h.AddRunningTimerAsync(user.Id, jobA.Id, _h.Now.AddMinutes(-35), machine.Id);
        var b = await _h.AddRunningTimerAsync(user.Id, jobB.Id, _h.Now.AddMinutes(-35), filler1.Id);
        var c = await _h.AddRunningTimerAsync(user.Id, jobC.Id, _h.Now.AddMinutes(-35), filler2.Id);

        await ClockOutAsync(user.Id);

        var stored = new[] { await StoredAsync(a.Id), await StoredAsync(b.Id), await StoredAsync(c.Id) };
        stored.Select(e => e.LaborMinutes).Should().Equal(3.18m, 15.91m, 15.91m);
        stored.Sum(e => e.LaborMinutes!.Value).Should().Be(35m);
        stored.Sum(e => e.LaborCost).Should().BeApproximately(35m / 60m * Rate, 0.0001m);
        stored.Should().OnlyContain(e => e.AllocationMethod == LaborAllocationMethod.Weighted && e.AllocatedAt == _h.Now);
        stored[0].BurdenCost.Should().BeApproximately(35m / 60m * Burden, 0.0001m, "a Machine-basis work center burdens the full cycle");
        stored[1].BurdenCost.Should().BeApproximately(15.91m / 60m * Burden, 0.0001m, "a Labor-basis work center burdens the labor share");
    }

    [Fact]
    public async Task QuickBooks_gets_the_labor_share_not_the_elapsed_time()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-QBO");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-90));
        var first = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60));
        var second = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddMinutes(-60));

        await ClockOutAsync(user.Id);

        var payloads = await _h.Db.SyncQueueEntries.AsNoTracking()
            .Where(q => q.Operation == "CreateTimeActivity")
            .ToListAsync();
        payloads.Should().HaveCount(2);
        payloads.Select(q => JsonSerializer.Deserialize<AccountingTimeActivity>(q.Payload!)!.Hours)
            .Should().AllSatisfy(hours => hours.Should().Be(0.5m));
        (await StoredAsync(first.Id)).LaborMinutes.Should().Be(30m);
        (await StoredAsync(second.Id)).LaborMinutes.Should().Be(30m);
    }

    [Fact]
    public async Task An_unpaid_break_removes_labor_from_the_running_timers()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-BREAK");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-90));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-60));
        await _h.AddClockEventAsync(user.Id, "BreakEnd", _h.Now.AddMinutes(-50));
        var first = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-80));
        var second = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-80));

        await ClockOutAsync(user.Id);

        (await StoredAsync(first.Id)).LaborMinutes.Should().Be(35m);
        (await StoredAsync(second.Id)).LaborMinutes.Should().Be(35m);
    }

    [Fact]
    public async Task Paid_breaks_keep_their_labor_when_the_setting_is_off()
    {
        _h.BreaksPauseLabor = false;
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-PAIDBREAK");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-90));
        await _h.AddClockEventAsync(user.Id, "BreakStart", _h.Now.AddMinutes(-60));
        await _h.AddClockEventAsync(user.Id, "BreakEnd", _h.Now.AddMinutes(-50));
        var only = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-80));

        await ClockOutAsync(user.Id);

        var stored = await StoredAsync(only.Id);
        stored.LaborMinutes.Should().BeNull("a lone, fully paid timer keeps its plain duration");
        stored.DurationMinutes.Should().Be(80);
    }

    [Fact]
    public async Task A_single_timer_with_no_overlap_costs_exactly_as_before()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-SINGLE");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-45).AddSeconds(-20));

        await ClockOutAsync(user.Id);

        var stored = await StoredAsync(entry.Id);
        stored.LaborMinutes.Should().BeNull();
        stored.AllocationMethod.Should().Be(LaborAllocationMethod.Weighted);
        stored.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task Labor_split_None_charges_every_timer_its_full_time_like_before()
    {
        _h.LaborSplit = ShopFloorSettings.LaborSplitNone;
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-NONE");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-60));
        var first = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));
        var second = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));

        await ClockOutAsync(user.Id);

        foreach (var id in new[] { first.Id, second.Id })
        {
            var stored = await StoredAsync(id);
            stored.LaborMinutes.Should().BeNull();
            stored.AllocationMethod.Should().Be(LaborAllocationMethod.None);
        }
    }

    [Fact]
    public async Task A_locked_stopping_entry_keeps_a_null_labor_share()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-LOCKED");
        var locked = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));
        await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));
        locked.IsLocked = true;
        await _h.Db.SaveChangesAsync();

        await ClockOutAsync(user.Id);

        var stored = await StoredAsync(locked.Id);
        stored.LaborMinutes.Should().BeNull();
        stored.AllocationMethod.Should().BeNull();
    }

    [Fact]
    public async Task A_timer_stopped_before_its_sibling_shares_only_the_overlap_and_both_add_up_to_the_clock()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-STAGGER");
        await _h.AddClockEventAsync(user.Id, "ClockIn", _h.Now.AddMinutes(-120));
        var early = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60));
        var late = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60));
        var stopNow = _h.Now;
        _h.Now = stopNow.AddMinutes(-30);
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(user.Id, _h.Now, TimeEntryId: early.Id), CancellationToken.None);
        _h.Now = stopNow;

        await ClockOutAsync(user.Id);

        (await StoredAsync(early.Id)).LaborMinutes.Should().Be(15m);
        (await StoredAsync(late.Id)).LaborMinutes.Should().Be(45m);
    }

    private async Task<(TimeEntry Short, TimeEntry Long)> TwoStoppedSiblingsAsync(int userId, int jobId)
    {
        var shortOne = await _h.AddRunningTimerAsync(userId, jobId, _h.Now.AddMinutes(-60));
        var longOne = await _h.AddRunningTimerAsync(userId, jobId, _h.Now.AddMinutes(-60));
        var stopNow = _h.Now;
        _h.Now = stopNow.AddMinutes(-30);
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(userId, _h.Now, TimeEntryId: shortOne.Id), CancellationToken.None);
        _h.Now = stopNow;
        await _h.StopActiveTimerHandler().Handle(new StopActiveTimerCommand(userId, _h.Now, TimeEntryId: longOne.Id), CancellationToken.None);
        return (shortOne, longOne);
    }

    private UpdateTimeEntryHandler UpdateHandler(int userId)
        => new(_h.Repo, _h.Db, _h.Clock.Object, TestCaller.As(userId), new JobCostService(_h.Db), _h.LaborAllocation());

    [Fact]
    public async Task Editing_one_stop_time_reallocates_its_sibling()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-EDIT");
        var (shortOne, longOne) = await TwoStoppedSiblingsAsync(user.Id, job.Id);
        (await StoredAsync(longOne.Id)).LaborMinutes.Should().Be(45m);

        await UpdateHandler(user.Id).Handle(
            new UpdateTimeEntryCommand(shortOne.Id, new UpdateTimeEntryRequestModel(null, null, 10, null, null)), CancellationToken.None);

        (await StoredAsync(shortOne.Id)).LaborMinutes.Should().Be(5m);
        (await StoredAsync(longOne.Id)).LaborMinutes.Should().Be(55m);
    }

    [Fact]
    public async Task An_edit_that_would_change_a_locked_sibling_is_refused()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-EDIT-LOCK");
        var (shortOne, longOne) = await TwoStoppedSiblingsAsync(user.Id, job.Id);
        var tracked = await _h.Db.TimeEntries.SingleAsync(t => t.Id == longOne.Id);
        tracked.IsLocked = true;
        await _h.Db.SaveChangesAsync();

        var act = () => UpdateHandler(user.Id).Handle(
            new UpdateTimeEntryCommand(shortOne.Id, new UpdateTimeEntryRequestModel(null, null, 10, null, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*locked pay period*");
        (await StoredAsync(longOne.Id)).LaborMinutes.Should().Be(45m);
    }

    [Fact]
    public async Task Deleting_a_timer_gives_its_sibling_the_whole_clocked_time_back()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-DELETE");
        var (shortOne, longOne) = await TwoStoppedSiblingsAsync(user.Id, job.Id);

        await new DeleteTimeEntryHandler(_h.Repo, TestCaller.As(user.Id), _h.Db, _h.Clock.Object, _h.LaborAllocation())
            .Handle(new DeleteTimeEntryCommand(shortOne.Id), CancellationToken.None);

        var sibling = await StoredAsync(longOne.Id);
        sibling.LaborMinutes.Should().BeNull("it no longer overlaps anything");
        sibling.LaborCost.Should().Be(0m, "the job has no labor rate in this harness");
    }

    [Fact]
    public async Task Recompute_previews_without_writing_then_applies_and_skips_locked_entries()
    {
        _h.LaborSplit = ShopFloorSettings.LaborSplitNone;
        var user = await _h.AddUserAsync();
        var (_, filler, _, _) = await SeedRoutingAsync();
        var job = await _h.AddJobAsync("J-RECOMPUTE");
        var first = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60), filler.Id);
        var second = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60), filler.Id);
        var lockedOne = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-60), filler.Id);
        await ClockOutAsync(user.Id);
        var trackedLocked = await _h.Db.TimeEntries.SingleAsync(t => t.Id == lockedOne.Id);
        trackedLocked.IsLocked = true;
        await _h.Db.SaveChangesAsync();
        _h.Db.ChangeTracker.Clear();
        (await StoredAsync(first.Id)).LaborCost.Should().Be(30m, "None charged the full hour");
        _h.LaborSplit = ShopFloorSettings.LaborSplitWeighted;
        var today = DateOnly.FromDateTime(_h.Now.UtcDateTime);

        var preview = await _h.LaborAllocation().RecomputeAsync(null, today, today, apply: false, CancellationToken.None);

        preview.Applied.Should().BeFalse();
        preview.LockedSkipped.Should().Be(1);
        preview.Jobs.Should().ContainSingle();
        preview.Jobs[0].LaborMinutesBefore.Should().Be(120m);
        preview.Jobs[0].LaborMinutesAfter.Should().Be(40m);
        preview.Jobs[0].LaborCostBefore.Should().Be(60m);
        preview.Jobs[0].LaborCostAfter.Should().BeApproximately(20m, 0.0001m);
        (await StoredAsync(first.Id)).LaborCost.Should().Be(30m);

        var applied = await _h.LaborAllocation().RecomputeAsync(null, today, today, apply: true, CancellationToken.None);

        applied.Applied.Should().BeTrue();
        (await StoredAsync(first.Id)).LaborMinutes.Should().Be(20m);
        (await StoredAsync(second.Id)).LaborMinutes.Should().Be(20m);
        (await StoredAsync(lockedOne.Id)).LaborMinutes.Should().BeNull();
        (await StoredAsync(lockedOne.Id)).LaborCost.Should().Be(30m);
        (await _h.Db.ActivityLogs.AsNoTracking().CountAsync(a => a.Action == "labor-recomputed" && a.EntityId == job.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Recompute_caps_only_the_days_the_person_used_the_clock()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-TWO-DAYS");
        var yesterday = _h.Now.AddDays(-1);
        await _h.AddClockEventAsync(user.Id, "ClockIn", yesterday.AddMinutes(-30));
        await _h.AddClockEventAsync(user.Id, "ClockOut", yesterday);
        var ids = new List<int>();
        foreach (var start in new[] { yesterday.AddMinutes(-60), yesterday.AddMinutes(-60), _h.Now.AddMinutes(-60), _h.Now.AddMinutes(-60) })
        {
            var entry = new TimeEntry
            {
                UserId = user.Id, JobId = job.Id, Date = DateOnly.FromDateTime(start.UtcDateTime),
                TimerStart = start, TimerStop = start.AddMinutes(60), DurationMinutes = 60,
            };
            _h.Db.TimeEntries.Add(entry);
            await _h.Db.SaveChangesAsync();
            ids.Add(entry.Id);
        }

        await _h.LaborAllocation().RecomputeAsync(user.Id, DateOnly.FromDateTime(yesterday.UtcDateTime), DateOnly.FromDateTime(_h.Now.UtcDateTime),
            apply: true, CancellationToken.None);

        var labor = new List<decimal?>();
        foreach (var id in ids)
            labor.Add((await StoredAsync(id)).LaborMinutes);
        labor.Should().Equal(15m, 15m, 30m, 30m);
    }

    [Fact]
    public async Task Resuming_a_stopped_timer_clears_its_split_and_cost()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-RESUME");
        var entry = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));
        await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddMinutes(-30));
        await ClockOutAsync(user.Id);

        await new ResumeStoppedTimerHandler(_h.Repo, _h.Db, _h.TimerHub.Object)
            .Handle(new ResumeStoppedTimerCommand(user.Id, _h.Now), CancellationToken.None);

        var stored = await StoredAsync(entry.Id);
        stored.TimerStop.Should().BeNull();
        stored.LaborMinutes.Should().BeNull();
        stored.AllocationMethod.Should().BeNull();
    }
}
