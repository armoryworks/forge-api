using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Sequences;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Sequences;

namespace Forge.Tests.Sequences;

/// <summary>Built-in gate sources + the clock job + the approval reaction, end to end through the real evaluation service.</summary>
public class SequenceGateSourceTests
{
    private const int U = SequenceHandlerFixture.UserId;

    private static async Task<SequenceInstanceResponseModel> StartWithGate(SequenceHandlerFixture f, SequenceGateDefinitionModel gate, string? subjectType = "Lot", int? subjectId = 9)
    {
        var model = new SequenceDefinitionRequestModel("g", "Gate test", null, null,
            [new("a", "A", null, 0), new("b", "B", null, 1)], [new("a", "b")], [gate]);
        var d = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(model), default);
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(d.Id, U), default);
        var i = await new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock)
            .Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(d.Id, null, subjectType, subjectId), U), default);
        return await new CompleteSequenceStepHandler(f.Db, f.Evaluation, f.Clock).Handle(new CompleteSequenceStepCommand(i.Id, "a", U), default);
    }

    private static SequenceStepInstanceResponseModel B(SequenceInstanceResponseModel i) => i.Steps.First(s => s.StepKey == "b");

    [Fact]
    public async Task Time_window_gate_opens_and_closes_with_the_clock_via_the_clock_job()
    {
        await using var f = new SequenceHandlerFixture();
        var opens = f.Clock.UtcNow.AddHours(1); var closes = f.Clock.UtcNow.AddHours(3);
        var i = await StartWithGate(f, new("b", "window", "Permit window", SequenceGateSourceType.TimeWindow,
            $"{{\"notBefore\":\"{opens:O}\",\"notAfter\":\"{closes:O}\"}}"));
        B(i).IsBlocked.Should().BeTrue();
        B(i).BlockedReason.Should().Contain("Window opens");

        var job = new SequenceClockJob(f.Db, f.Evaluation, f.Clock, f.Publisher.Object, NullLogger<SequenceClockJob>.Instance);
        f.Clock.Advance(TimeSpan.FromHours(2)); // inside the window
        await job.ExecuteAsync(default);
        (await Get(f, i.Id)).Steps.First(s => s.StepKey == "b").Status.Should().Be(SequenceStepStatus.Ready);

        f.Clock.Advance(TimeSpan.FromHours(2)); // past notAfter
        await job.ExecuteAsync(default);
        var after = (await Get(f, i.Id)).Steps.First(s => s.StepKey == "b");
        after.Status.Should().Be(SequenceStepStatus.Pending);
        after.BlockedReason.Should().Contain("Window closed");
    }

    [Fact]
    public async Task Resource_clock_gate_blocks_once_the_subjects_clock_expires_and_the_job_fires_it_exactly_once()
    {
        await using var f = new SequenceHandlerFixture();
        await new CreateSequenceResourceClockHandler(f.Db, f.Clock).Handle(new CreateSequenceResourceClockCommand(
            new SequenceResourceClockRequestModel("Lot", 9, f.Clock.UtcNow.AddDays(2), SequenceExpiryAction.Escalate, "QA")), default);
        var i = await StartWithGate(f, new("b", "fresh", "Lot unexpired", SequenceGateSourceType.ResourceClock, "{\"fromSubject\":true}"));
        B(i).Status.Should().Be(SequenceStepStatus.Ready);
        i.Gates.Single().Reason.Should().StartWith("Expires");

        var job = new SequenceClockJob(f.Db, f.Evaluation, f.Clock, f.Publisher.Object, NullLogger<SequenceClockJob>.Instance);
        f.Clock.Advance(TimeSpan.FromDays(3));
        await job.ExecuteAsync(default);
        await job.ExecuteAsync(default); // second pass must not re-fire

        f.Published.OfType<SequenceClockExpiredEvent>().Should().ContainSingle(e => e.ClockKind == "resource" && e.ResourceId == 9 && e.EscalateRole == "QA");
        var b = (await Get(f, i.Id)).Steps.First(s => s.StepKey == "b");
        b.Status.Should().Be(SequenceStepStatus.Pending);
        b.BlockedReason.Should().Contain("expired");
        (await new GetSequenceResourceClocksHandler(f.Db, f.Clock).Handle(new GetSequenceResourceClocksQuery("Lot", 9, IncludeFired: true), default))
            .Single().FiredAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Dwell_clock_fires_once_and_escalates_to_the_configured_role()
    {
        await using var f = new SequenceHandlerFixture();
        var model = new SequenceDefinitionRequestModel("dwell", "Dwell", null, null,
            [new("hold", "Holding", null, 0, MaxDwellMinutes: 10, DwellExpiryAction: SequenceExpiryAction.Escalate, EscalateRole: "Lead")], [], []);
        var d = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(model), default);
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(d.Id, U), default);
        var i = await new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock).Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(d.Id, null, null, null), U), default);
        await new StartSequenceStepHandler(f.Db, f.Evaluation, f.Clock).Handle(new StartSequenceStepCommand(i.Id, "hold", U), default);

        var job = new SequenceClockJob(f.Db, f.Evaluation, f.Clock, f.Publisher.Object, NullLogger<SequenceClockJob>.Instance);
        f.Clock.Advance(TimeSpan.FromMinutes(11));
        await job.ExecuteAsync(default);
        await job.ExecuteAsync(default);

        f.Published.OfType<SequenceClockExpiredEvent>().Should().ContainSingle(e => e.ClockKind == "dwell" && e.StepKey == "hold" && e.EscalateRole == "Lead");
        var events = await new GetSequenceEventsHandler(f.Db).Handle(new GetSequenceEventsQuery(i.Id), default);
        events.Count(e => e.Type == SequenceEventType.ClockExpired).Should().Be(1);
        events.Count(e => e.Type == SequenceEventType.Escalated).Should().Be(1);
    }

    [Fact]
    public async Task Approval_gate_goes_when_the_subjects_approval_completes_and_the_reaction_reevaluates()
    {
        await using var f = new SequenceHandlerFixture();
        f.Db.ApprovalWorkflows.Add(new ApprovalWorkflow { Id = 1, Name = "wf", EntityType = "Job" });
        f.Db.ApprovalRequests.Add(new ApprovalRequest { Id = 1, WorkflowId = 1, EntityType = "Job", EntityId = 5, Status = ApprovalRequestStatus.Pending, RequestedAt = f.Clock.UtcNow });
        await f.Db.SaveChangesAsync();
        var i = await StartWithGate(f, new("b", "signoff", "Engineering sign-off", SequenceGateSourceType.Approval, "{\"fromSubject\":true}"), "Job", 5);
        B(i).BlockedReason.Should().Contain("Approval Pending");

        var req = f.Db.ApprovalRequests.First();
        req.Status = ApprovalRequestStatus.Approved; req.CompletedAt = f.Clock.UtcNow;
        await f.Db.SaveChangesAsync();

        // the reaction dispatches ReevaluateSequenceCommand through MediatR — run the handler directly here
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ReevaluateSequenceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ReevaluateSequenceCommand, CancellationToken>((c, ct) => new ReevaluateSequenceHandler(f.Db, f.Evaluation).Handle(c, ct));
        await new OnApprovalCompleted_ReevaluateSequences(f.Db, mediator.Object).Handle(new ApprovalCompletedEvent("Job", 5, true, U, null), default);

        (await Get(f, i.Id)).Steps.First(s => s.StepKey == "b").Status.Should().Be(SequenceStepStatus.Ready);
    }

    [Fact]
    public async Task Custom_gate_with_no_registered_source_fails_closed_and_a_registered_one_is_consulted()
    {
        await using var unknown = new SequenceHandlerFixture();
        var i = await StartWithGate(unknown, new("b", "mat", "Materials", SequenceGateSourceType.Custom, "{\"key\":\"materials-ready\"}"));
        B(i).IsBlocked.Should().BeTrue();
        B(i).BlockedReason.Should().Contain("No gate source registered");

        await using var known = new SequenceHandlerFixture(extraSources: new StubMaterialsGate());
        var j = await StartWithGate(known, new("b", "mat", "Materials", SequenceGateSourceType.Custom, "{\"key\":\"materials-ready\"}"));
        B(j).Status.Should().Be(SequenceStepStatus.Ready);
        j.Gates.Single().Reason.Should().Be("All BOM lines issued");
    }

    private static Task<SequenceInstanceResponseModel> Get(SequenceHandlerFixture f, int id) =>
        new GetSequenceInstanceHandler(f.Db).Handle(new GetSequenceInstanceQuery(id), default);

    private sealed class StubMaterialsGate : IGateSource
    {
        public SequenceGateSourceType SourceType => SequenceGateSourceType.Custom;
        public string? CustomKey => "materials-ready";
        public Task<SequenceGateVerdictResult> EvaluateAsync(SequenceGateContext context, CancellationToken cancellationToken) =>
            Task.FromResult(SequenceGateVerdictResult.Go("All BOM lines issued"));
    }
}
