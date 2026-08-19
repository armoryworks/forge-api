using FluentAssertions;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Sequences;

public class SequenceInstanceHandlerTests
{
    private const int U = SequenceHandlerFixture.UserId;

    private static async Task<int> PublishedSerial(SequenceHandlerFixture f, params SequenceGateDefinitionModel[] gates)
    {
        var model = new SequenceDefinitionRequestModel("job-basic", "Basic routing", null, "Job",
            [new("cut", "Cut", null, 0), new("inspect", "Inspect", null, 1, MaxDwellMinutes: 30, DwellExpiryAction: SequenceExpiryAction.Escalate, EscalateRole: "Supervisor"), new("ship", "Ship", null, 2)],
            [new("cut", "inspect"), new("inspect", "ship")],
            gates.Length > 0 ? gates : [new("inspect", "qc", "First article", SequenceGateSourceType.ManualClearance)]);
        var d = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(model), default);
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(d.Id, U), default);
        return d.Id;
    }

    private static Task<SequenceInstanceResponseModel> Start(SequenceHandlerFixture f, int defId, int? subjectId = 42) =>
        new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock).Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(defId, null, "Job", subjectId), U), default);

    private static SequenceStepInstanceResponseModel Step(SequenceInstanceResponseModel i, string key) => i.Steps.First(s => s.StepKey == key);

    [Fact]
    public async Task Start_makes_the_first_step_ready_and_a_manual_gate_blocks_the_second_until_cleared()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var i = await Start(f, defId);

        i.Status.Should().Be(SequenceInstanceStatus.Running);
        Step(i, "cut").Status.Should().Be(SequenceStepStatus.Ready);
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.Pending);
        f.Published.OfType<SequenceStepReadyEvent>().Should().ContainSingle(e => e.StepKey == "cut" && e.SubjectEntityId == 42);

        i = await new CompleteSequenceStepHandler(f.Db, f.Evaluation, f.Clock).Handle(new CompleteSequenceStepCommand(i.Id, "cut", U), default);
        Step(i, "cut").Status.Should().Be(SequenceStepStatus.Complete);
        Step(i, "inspect").IsBlocked.Should().BeTrue();
        Step(i, "inspect").BlockedReason.Should().Contain("First article").And.Contain("Awaiting clearance");

        i = await new ClearSequenceGateHandler(f.Db, f.Evaluation, f.Clock).Handle(new ClearSequenceGateCommand(i.Id, "inspect", "qc", U), default);
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.Ready);
        i.Gates.Single().Verdict.Should().Be(SequenceGateVerdict.Go);
        i.Gates.Single().ClearedByUserId.Should().Be(U);
    }

    [Fact]
    public async Task Complete_every_step_completes_the_instance_and_the_event_log_tells_the_story()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var i = await Start(f, defId);
        var complete = new CompleteSequenceStepHandler(f.Db, f.Evaluation, f.Clock);
        var start = new StartSequenceStepHandler(f.Db, f.Evaluation, f.Clock);

        await complete.Handle(new CompleteSequenceStepCommand(i.Id, "cut", U), default);
        await new ClearSequenceGateHandler(f.Db, f.Evaluation, f.Clock).Handle(new ClearSequenceGateCommand(i.Id, "inspect", "qc", U), default);
        i = await start.Handle(new StartSequenceStepCommand(i.Id, "inspect", U), default);
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.InProgress);
        Step(i, "inspect").DwellExpiresAt.Should().Be(f.Clock.UtcNow.AddMinutes(30));
        await complete.Handle(new CompleteSequenceStepCommand(i.Id, "inspect", U), default);
        i = await complete.Handle(new CompleteSequenceStepCommand(i.Id, "ship", U), default);

        i.Status.Should().Be(SequenceInstanceStatus.Completed);
        f.Published.OfType<SequenceInstanceCompletedEvent>().Should().ContainSingle();

        var events = await new GetSequenceEventsHandler(f.Db).Handle(new GetSequenceEventsQuery(i.Id), default);
        events.Select(e => e.Type).Should().ContainInOrder(
            SequenceEventType.InstanceStarted, SequenceEventType.StepReady, SequenceEventType.StepCompleted,
            SequenceEventType.GateCleared, SequenceEventType.GateEvaluated, SequenceEventType.StepReady,
            SequenceEventType.StepStarted, SequenceEventType.StepCompleted, SequenceEventType.StepReady,
            SequenceEventType.StepCompleted, SequenceEventType.InstanceCompleted);

        // a completed run refuses further step commands
        var again = () => complete.Handle(new CompleteSequenceStepCommand(i.Id, "ship", U), default);
        await again.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Override_forces_a_gate_go_with_a_reason_and_skip_counts_as_done()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var i = await Start(f, defId);
        await new CompleteSequenceStepHandler(f.Db, f.Evaluation, f.Clock).Handle(new CompleteSequenceStepCommand(i.Id, "cut", U), default);

        var noReason = () => new OverrideSequenceGateHandler(f.Db, f.Evaluation, f.Clock).Handle(new OverrideSequenceGateCommand(i.Id, "inspect", "qc", " ", U), default);
        await noReason.Should().ThrowAsync<InvalidOperationException>();

        i = await new OverrideSequenceGateHandler(f.Db, f.Evaluation, f.Clock).Handle(new OverrideSequenceGateCommand(i.Id, "inspect", "qc", "Supervisor waived FAI", U), default);
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.Ready);
        i.Gates.Single().OverrideReason.Should().Be("Supervisor waived FAI");

        i = await new SkipSequenceStepHandler(f.Db, f.Evaluation, f.Clock).Handle(new SkipSequenceStepCommand(i.Id, "inspect", "Customer waived inspection", U), default);
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.Skipped);
        Step(i, "ship").Status.Should().Be(SequenceStepStatus.Ready, "a skipped predecessor counts as done");
    }

    [Fact]
    public async Task Rework_resets_the_target_and_everything_downstream_including_clearances_and_overrides()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var i = await Start(f, defId);
        var complete = new CompleteSequenceStepHandler(f.Db, f.Evaluation, f.Clock);
        await complete.Handle(new CompleteSequenceStepCommand(i.Id, "cut", U), default);
        await new ClearSequenceGateHandler(f.Db, f.Evaluation, f.Clock).Handle(new ClearSequenceGateCommand(i.Id, "inspect", "qc", U), default);
        await complete.Handle(new CompleteSequenceStepCommand(i.Id, "inspect", U), default);

        i = await new ReworkSequenceHandler(f.Db, f.Evaluation, f.Clock).Handle(new ReworkSequenceCommand(i.Id, "cut", "Wrong material", U), default);
        Step(i, "cut").Status.Should().Be(SequenceStepStatus.Ready, "cut is a start step, so it is immediately ready again");
        Step(i, "inspect").Status.Should().Be(SequenceStepStatus.Pending);
        Step(i, "inspect").CompletedAt.Should().BeNull();
        i.Gates.Single().ClearedAt.Should().BeNull("clearances downstream of the rework point are void");
        i.Gates.Single().Verdict.Should().Be(SequenceGateVerdict.NoGo, "re-evaluated: awaiting clearance again");
        (await new GetSequenceEventsHandler(f.Db).Handle(new GetSequenceEventsQuery(i.Id), default))
            .Should().Contain(e => e.Type == SequenceEventType.Reworked && e.PayloadJson!.Contains("Wrong material"));
    }

    [Fact]
    public async Task Cancel_is_terminal_and_requires_a_reason()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var i = await Start(f, defId);
        var h = new CancelSequenceInstanceHandler(f.Db, f.Clock);
        await (((Func<Task>)(() => h.Handle(new CancelSequenceInstanceCommand(i.Id, "", U), default))).Should().ThrowAsync<InvalidOperationException>());
        i = await h.Handle(new CancelSequenceInstanceCommand(i.Id, "Order withdrawn", U), default);
        i.Status.Should().Be(SequenceInstanceStatus.Cancelled);
        i.CancelReason.Should().Be("Order withdrawn");
    }

    [Fact]
    public async Task Start_refuses_drafts_and_resolves_latest_published_by_code()
    {
        await using var f = new SequenceHandlerFixture();
        var defId = await PublishedSerial(f);
        var draft = await new NewSequenceDefinitionVersionHandler(f.Db).Handle(new NewSequenceDefinitionVersionCommand(defId), default);
        var startDraft = () => Start(f, draft.Id);
        await startDraft.Should().ThrowAsync<InvalidOperationException>();

        var byCode = await new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock)
            .Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(null, "job-basic", "Job", 1), U), default);
        byCode.DefinitionId.Should().Be(defId);
        byCode.DefinitionVersion.Should().Be(1);
    }
}
