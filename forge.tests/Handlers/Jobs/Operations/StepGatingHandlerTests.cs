using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class StepGatingHandlerTests
{
    private readonly JobOperationTestHarness _h = new();

    private Task<JobOperationProgressResponseModel> ReportAsync(
        int userId, int jobId, int operationId, decimal? completed, JobOperationStatus? status = null,
        bool acknowledge = false, string? reason = null, decimal? scrap = null)
        => _h.ProgressHandler(userId).Handle(
            new UpdateJobOperationProgressCommand(jobId, operationId, new UpdateJobOperationProgressRequestModel(
                completed, scrap, status, AcknowledgeGate: acknowledge, OverrideReason: reason)),
            CancellationToken.None);

    private async Task<(Job Job, List<Operation> Routing, int UserId)> SeedAsync(decimal quantity = 20m)
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(quantity);
        var user = await _h.Timers.AddUserAsync();
        return (job, routing, user.Id);
    }

    private async Task LinkAsync(Operation operation, Operation predecessor, PredecessorGate gate = PredecessorGate.Quantity)
    {
        _h.Timers.Db.OperationPredecessors.Add(new OperationPredecessor
        {
            OperationId = operation.Id,
            PredecessorOperationId = predecessor.Id,
            Gate = gate,
        });
        await _h.Timers.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Off_lets_a_step_report_more_than_its_predecessor_completed()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);

        var result = await ReportAsync(user, job.Id, routing[1].Id, 12m);

        result.Operation.CompletedQuantity.Should().Be(12m);
        result.Operation.Available.Should().BeNull("gating is off, so nothing is worked out");
    }

    [Fact]
    public async Task Warn_without_acknowledgement_refuses_and_records_nothing()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Warn";

        var act = () => ReportAsync(user, job.Id, routing[1].Id, 6m);

        var thrown = (await act.Should().ThrowAsync<StepGateException>()).Which;
        thrown.Violation.Mode.Should().Be(StepGatingMode.Warn);
        thrown.Violation.Gate.Should().Be(PredecessorGate.Quantity);
        thrown.Violation.PredecessorStep.Should().Be(10, "with no links a step follows the previous step");
        thrown.Violation.Available.Should().Be(5m);
        thrown.Violation.Requested.Should().Be(6m);
        thrown.Violation.CanOverride.Should().BeTrue();
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync(r => r.OperationId == routing[1].Id);
        row.CompletedQuantity.Should().Be(0m);
        (await _h.Timers.Db.JobOperationEvents.AnyAsync(e => e.JobOperationId == row.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Warn_acknowledged_records_the_pieces_and_logs_the_reason_on_the_event()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Warn";

        var result = await ReportAsync(user, job.Id, routing[1].Id, 6m, acknowledge: true, reason: "count at saw was short");

        result.Operation.CompletedQuantity.Should().Be(6m);
        var evt = await _h.Timers.Db.JobOperationEvents.AsNoTracking()
            .SingleAsync(e => e.JobOperation.OperationId == routing[1].Id);
        evt.GateOverride.Should().BeTrue();
        evt.GateOverrideReason.Should().Be("count at saw was short");
        (await _h.Timers.Db.JobActivityLogs.AsNoTracking()
                .Where(l => l.OperationId == routing[1].Id).Select(l => l.Description).ToListAsync())
            .Should().ContainSingle(d => d.Contains("Step gate warning acknowledged: count at saw was short"));
    }

    [Fact]
    public async Task Reporting_within_the_available_pieces_passes_every_mode()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 8m);
        _h.StepGating = "Block";

        var result = await ReportAsync(user, job.Id, routing[1].Id, 6m, scrap: 2m);

        result.Operation.CompletedQuantity.Should().Be(6m);
        result.Operation.Available.Should().Be(8m);
        result.Operation.AvailableFromStep.Should().Be(10);
        (await _h.Timers.Db.JobOperationEvents.AnyAsync(e => e.GateOverride)).Should().BeFalse();
    }

    [Fact]
    public async Task Block_refuses_a_worker_even_with_a_reason()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Block";
        _h.Roles = ["ProductionWorker"];

        var act = () => ReportAsync(user, job.Id, routing[1].Id, 9m, acknowledge: true, reason: "trust me");

        var thrown = (await act.Should().ThrowAsync<StepGateException>()).Which;
        thrown.Violation.Mode.Should().Be(StepGatingMode.Block);
        thrown.Violation.CanOverride.Should().BeFalse();
        thrown.Message.Should().Contain("A manager or production manager can override it");
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Production Manager")]
    [InlineData("Admin")]
    public async Task Block_is_overridden_by_a_supervisor_role_with_a_reason(string role)
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Block";
        _h.Roles = [role];

        var result = await ReportAsync(user, job.Id, routing[1].Id, 9m, acknowledge: true, reason: "rework parts re-entered");

        result.Operation.CompletedQuantity.Should().Be(9m);
        var evt = await _h.Timers.Db.JobOperationEvents.AsNoTracking().SingleAsync(e => e.JobOperation.OperationId == routing[1].Id);
        evt.GateOverride.Should().BeTrue();
        evt.GateOverrideReason.Should().Be("rework parts re-entered");
        (await _h.Timers.Db.JobActivityLogs.AsNoTracking().AnyAsync(l => l.Description.Contains("Step gate overridden: rework parts re-entered")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Block_override_needs_a_reason()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Block";
        _h.Roles = ["Manager"];

        var act = () => ReportAsync(user, job.Id, routing[1].Id, 9m, acknowledge: true, reason: "  ");

        var thrown = (await act.Should().ThrowAsync<StepGateException>()).Which;
        thrown.Violation.CanOverride.Should().BeTrue();
        thrown.Message.Should().Contain("Give a reason to override");
    }

    [Fact]
    public async Task Block_override_is_off_when_the_capability_is_off()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        _h.StepGating = "Block";
        _h.Roles = ["Production Manager"];
        _h.GateOverrideEnabled = false;

        var act = () => ReportAsync(user, job.Id, routing[1].Id, 9m, acknowledge: true, reason: "needed");

        (await act.Should().ThrowAsync<StepGateException>()).Which.Violation.CanOverride.Should().BeFalse();
    }

    [Fact]
    public async Task The_complete_gate_keeps_a_step_from_being_marked_done_before_its_predecessor()
    {
        var (job, routing, user) = await SeedAsync();
        await LinkAsync(routing[2], routing[1], PredecessorGate.Complete);
        await ReportAsync(user, job.Id, routing[0].Id, 20m);
        await ReportAsync(user, job.Id, routing[1].Id, 20m);
        _h.StepGating = "Warn";

        var act = () => ReportAsync(user, job.Id, routing[2].Id, 20m, JobOperationStatus.Complete);

        var thrown = (await act.Should().ThrowAsync<StepGateException>()).Which;
        thrown.Violation.Gate.Should().Be(PredecessorGate.Complete);
        thrown.Violation.WaitingOnSteps.Should().Equal(20);

        await ReportAsync(user, job.Id, routing[1].Id, null, JobOperationStatus.Complete);
        var done = await ReportAsync(user, job.Id, routing[2].Id, 20m, JobOperationStatus.Complete);
        done.Operation.Status.Should().Be(JobOperationStatus.Complete);
    }

    [Fact]
    public async Task The_complete_gate_is_ignored_when_gating_is_off()
    {
        var (job, routing, user) = await SeedAsync();
        await LinkAsync(routing[2], routing[1], PredecessorGate.Complete);

        var done = await ReportAsync(user, job.Id, routing[2].Id, null, JobOperationStatus.Complete);

        done.Operation.Status.Should().Be(JobOperationStatus.Complete);
    }

    [Fact]
    public async Task A_join_is_limited_by_its_slowest_predecessor()
    {
        var (job, routing, user) = await SeedAsync();
        await LinkAsync(routing[1], routing[0]);
        await LinkAsync(routing[2], routing[0]);
        await LinkAsync(routing[2], routing[1]);
        await ReportAsync(user, job.Id, routing[0].Id, 15m);
        await ReportAsync(user, job.Id, routing[1].Id, 4m);
        _h.StepGating = "Block";

        var act = () => ReportAsync(user, job.Id, routing[2].Id, 5m);

        var thrown = (await act.Should().ThrowAsync<StepGateException>()).Which;
        thrown.Violation.Available.Should().Be(4m);
        thrown.Violation.PredecessorStep.Should().Be(20);
    }

    [Fact]
    public async Task A_skipped_predecessor_passes_its_own_predecessor_through()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 7m);
        await ReportAsync(user, job.Id, routing[1].Id, null, JobOperationStatus.Skipped);
        _h.StepGating = "Block";

        var ok = await ReportAsync(user, job.Id, routing[2].Id, 7m);
        ok.Operation.Available.Should().Be(7m);
        ok.Operation.AvailableFromStep.Should().Be(10);

        var act = () => ReportAsync(user, job.Id, routing[2].Id, 8m);
        (await act.Should().ThrowAsync<StepGateException>()).Which.Violation.PredecessorStep.Should().Be(10);
    }

    [Fact]
    public async Task The_part_setting_beats_the_shop_setting()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        var part = await _h.Timers.Db.Parts.SingleAsync(p => p.Id == job.PartId);

        _h.StepGating = "Off";
        part.StepGating = StepGatingMode.Block;
        await _h.Timers.Db.SaveChangesAsync();
        var blocked = () => ReportAsync(user, job.Id, routing[1].Id, 6m);
        (await blocked.Should().ThrowAsync<StepGateException>()).Which.Violation.Mode.Should().Be(StepGatingMode.Block);

        _h.StepGating = "Block";
        part.StepGating = StepGatingMode.Off;
        await _h.Timers.Db.SaveChangesAsync();
        (await ReportAsync(user, job.Id, routing[1].Id, 6m)).Operation.CompletedQuantity.Should().Be(6m);
    }

    [Fact]
    public async Task Lowering_a_count_is_never_gated()
    {
        var (job, routing, user) = await SeedAsync();
        await ReportAsync(user, job.Id, routing[0].Id, 5m);
        await ReportAsync(user, job.Id, routing[1].Id, 9m);
        _h.StepGating = "Block";

        var result = await ReportAsync(user, job.Id, routing[1].Id, 7m);

        result.Operation.CompletedQuantity.Should().Be(7m);
    }

    [Fact]
    public async Task Starting_a_timer_is_never_gated()
    {
        var (job, routing, user) = await SeedAsync();
        _h.StepGating = "Block";

        var started = await _h.StartHandler(user).Handle(
            new StartJobOperationTimerCommand(job.Id, routing[2].Id, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);

        started.AlreadyRunning.Should().BeFalse();
        started.Operation.Status.Should().Be(JobOperationStatus.InProgress);
        started.Operation.Available.Should().Be(0m, "step 20 has not completed anything yet");
    }

    [Fact]
    public async Task A_blocked_stop_with_pieces_leaves_the_timer_running()
    {
        var (job, routing, user) = await SeedAsync();
        await _h.StartHandler(user).Handle(
            new StartJobOperationTimerCommand(job.Id, routing[1].Id, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);
        _h.StepGating = "Block";

        var act = () => _h.StopHandler(user).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[1].Id, new StopJobOperationTimerRequestModel(GoodQuantity: 3m)),
            CancellationToken.None);

        await act.Should().ThrowAsync<StepGateException>();
        (await _h.Timers.Db.TimeEntries.AsNoTracking().SingleAsync()).TimerStop.Should().BeNull();
    }

    [Fact]
    public async Task A_stop_passes_the_acknowledgement_through_to_the_progress()
    {
        var (job, routing, user) = await SeedAsync();
        await _h.StartHandler(user).Handle(
            new StartJobOperationTimerCommand(job.Id, routing[1].Id, new StartJobOperationTimerRequestModel()),
            CancellationToken.None);
        _h.StepGating = "Warn";

        var stopped = await _h.StopHandler(user).Handle(
            new StopJobOperationTimerCommand(job.Id, routing[1].Id,
                new StopJobOperationTimerRequestModel(GoodQuantity: 3m, AcknowledgeGate: true, OverrideReason: "saw count pending")),
            CancellationToken.None);

        stopped.Operation!.CompletedQuantity.Should().Be(3m);
        (await _h.Timers.Db.TimeEntries.AsNoTracking().SingleAsync()).TimerStop.Should().NotBeNull();
        (await _h.Timers.Db.JobOperationEvents.AsNoTracking().SingleAsync()).GateOverrideReason.Should().Be("saw count pending");
    }

    [Fact]
    public async Task Step_rows_show_what_is_available_and_what_a_step_waits_on()
    {
        var (job, routing, user) = await SeedAsync();
        await LinkAsync(routing[2], routing[0], PredecessorGate.Complete);
        await ReportAsync(user, job.Id, routing[0].Id, 12m);
        _h.StepGating = "Warn";

        var built = await _h.Operations.BuildAsync(job.Id, CancellationToken.None);

        built.StepGating.Should().Be(StepGatingMode.Warn);
        var rows = built.Operations.ToDictionary(o => o.StepNumber);
        rows[10].Available.Should().BeNull();
        rows[20].Available.Should().Be(12m);
        rows[20].AvailableFromStep.Should().Be(10);
        rows[30].Available.Should().Be(12m, "step 30 follows step 10 directly");
        rows[30].WaitingOnSteps.Should().Equal(10);
    }
}
