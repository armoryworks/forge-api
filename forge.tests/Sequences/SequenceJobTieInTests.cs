using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Sequences;
using Forge.Api.Features.Sequences.GateSources;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Sequences;

/// <summary>Job-structure tie-ins: auto-start on job creation, job-stage gate, re-evaluation on stage change.</summary>
public class SequenceJobTieInTests
{
    private const int U = SequenceHandlerFixture.UserId;

    private static async Task SeedJob(SequenceHandlerFixture f, int jobId = 100, int stageId = 1)
    {
        if (!f.Db.TrackTypes.Any())
        {
            f.Db.TrackTypes.Add(new TrackType { Id = 1, Name = "Production", Code = "prod" });
            f.Db.JobStages.AddRange(
                new JobStage { Id = 1, TrackTypeId = 1, Name = "Queued", Code = "queued", SortOrder = 0 },
                new JobStage { Id = 2, TrackTypeId = 1, Name = "In Production", Code = "in-production", SortOrder = 1 },
                new JobStage { Id = 3, TrackTypeId = 1, Name = "Done", Code = "done", SortOrder = 2 });
        }
        f.Db.Jobs.Add(new Job { Id = jobId, JobNumber = $"J-{jobId}", Title = "Bracket run", TrackTypeId = 1, CurrentStageId = stageId });
        await f.Db.SaveChangesAsync();
    }

    private static async Task<int> PublishJobDefinition(SequenceHandlerFixture f, bool autoStart, params SequenceGateDefinitionModel[] gates)
    {
        var model = new SequenceDefinitionRequestModel("job-gates", "Job gates", null, "Job",
            [new("prep", "Prep", null, 0), new("ship", "Ship", null, 1)], [new("prep", "ship")], gates, AutoStartOnSubjectCreate: autoStart);
        var d = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(model), default);
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(d.Id, U), default);
        return d.Id;
    }

    private static Mock<IMediator> MediatorThatRuns(SequenceHandlerFixture f)
    {
        var m = new Mock<IMediator>();
        m.Setup(x => x.Send(It.IsAny<StartSequenceInstanceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<StartSequenceInstanceCommand, CancellationToken>((c, ct) => new StartSequenceInstanceHandler(f.Db, f.Evaluation, f.Clock).Handle(c, ct));
        m.Setup(x => x.Send(It.IsAny<ReevaluateSequenceCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ReevaluateSequenceCommand, CancellationToken>((c, ct) => new ReevaluateSequenceHandler(f.Db, f.Evaluation).Handle(c, ct));
        return m;
    }

    [Fact]
    public async Task A_new_job_auto_starts_flagged_definitions_once()
    {
        await using var f = new SequenceHandlerFixture();
        f.Sources.Add(new JobStageGateSource(f.Db));
        await SeedJob(f);
        await PublishJobDefinition(f, autoStart: true);
        var handler = new OnJobCreated_StartSequences(f.Db, MediatorThatRuns(f).Object);

        await handler.Handle(new JobCreatedEvent(100, U), default);
        await handler.Handle(new JobCreatedEvent(100, U), default); // replay must not start a second run

        var runs = await new GetSequenceInstancesHandler(f.Db).Handle(new GetSequenceInstancesQuery("Job", 100, null, null), default);
        runs.Should().ContainSingle();
        runs[0].Steps.First(s => s.StepKey == "prep").Status.Should().Be(SequenceStepStatus.Ready);
    }

    [Fact]
    public async Task Job_stage_gate_opens_when_the_job_reaches_the_stage_via_the_stage_changed_reaction()
    {
        await using var fx = new SequenceHandlerFixture();
        fx.Sources.Add(new JobStageGateSource(fx.Db));
        await SeedJob(fx);
        var defId = await PublishJobDefinition(fx, autoStart: false,
            new SequenceGateDefinitionModel("ship", "in-prod", "Job in production", SequenceGateSourceType.Custom, "{\"key\":\"job-stage\",\"stageCode\":\"in-production\"}"));
        var i = await new StartSequenceInstanceHandler(fx.Db, fx.Evaluation, fx.Clock)
            .Handle(new StartSequenceInstanceCommand(new StartSequenceRequestModel(defId, null, "Job", 100), U), default);
        await new CompleteSequenceStepHandler(fx.Db, fx.Evaluation, fx.Clock).Handle(new CompleteSequenceStepCommand(i.Id, "prep", U), default);

        var before = await new GetSequenceInstanceHandler(fx.Db).Handle(new GetSequenceInstanceQuery(i.Id), default);
        before.Steps.First(s => s.StepKey == "ship").BlockedReason.Should().Contain("needs In Production");

        fx.Db.Jobs.First(j => j.Id == 100).CurrentStageId = 2;
        await fx.Db.SaveChangesAsync();
        await new OnJobStageChanged_ReevaluateSequences(fx.Db, MediatorThatRuns(fx).Object).Handle(new JobStageChangedEvent(100, 1, 2, U), default);

        var after = await new GetSequenceInstanceHandler(fx.Db).Handle(new GetSequenceInstanceQuery(i.Id), default);
        after.Steps.First(s => s.StepKey == "ship").Status.Should().Be(SequenceStepStatus.Ready);
    }
}
