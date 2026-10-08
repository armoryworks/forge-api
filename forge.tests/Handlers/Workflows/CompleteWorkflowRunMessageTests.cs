using FluentAssertions;
using Moq;

using Forge.Api.Features.Workflows.Runs;
using Forge.Api.Services;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Workflows;

public class CompleteWorkflowRunMessageTests
{
    private const int RunId = 4711;
    private const int PartId = 9182;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IEntityReadinessService> _readiness = new();
    private readonly CompleteWorkflowRunHandler _handler;

    public CompleteWorkflowRunMessageTests()
    {
        _db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            DefinitionId = "part-make-component-v1",
            EntityType = "Part",
            DefaultMode = "guided",
            StepsJson = """
            [
              {"id":"basics","labelKey":"workflow.parts.steps.basics","componentName":"PartBasicsStepComponent","required":true,"completionGates":["hasBasics"]},
              {"id":"routing","labelKey":"workflow.parts.steps.routing","componentName":"PartRoutingStepComponent","required":true,"completionGates":["hasRouting"]}
            ]
            """,
        });
        _db.EntityReadinessValidators.Add(new EntityReadinessValidator
        {
            EntityType = "Part",
            ValidatorId = "hasBasics",
            DisplayNameKey = "validators.parts.hasBasics",
            MissingMessageKey = "validators.parts.hasBasicsMissing",
        });
        _db.SaveChanges();

        _handler = new CompleteWorkflowRunHandler(
            _db,
            _readiness.Object,
            [],
            Mock.Of<ISystemAuditWriter>(),
            new SystemClock());
    }

    private void SeedRun(int? entityId)
    {
        _db.WorkflowRuns.Add(new WorkflowRun
        {
            Id = RunId,
            EntityType = "Part",
            EntityId = entityId,
            DefinitionId = "part-make-component-v1",
            CurrentStepId = "basics",
            Mode = "guided",
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task MissingRouting_NamesTheStepInPlainWords()
    {
        SeedRun(PartId);
        _readiness
            .Setup(r => r.GetMissingValidatorsAsync("Part", PartId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EntityReadinessValidator
                {
                    EntityType = "Part",
                    ValidatorId = "hasRouting",
                    DisplayNameKey = "validators.parts.hasRouting",
                    MissingMessageKey = "validators.parts.hasRoutingMissing",
                },
            ]);

        var act = () => _handler.Handle(new CompleteWorkflowRunCommand(RunId), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<WorkflowMissingValidatorsException>()).Which;
        ex.Message.Should().Be("Finish these before this part can be active: Routing.");
        ex.Message.Should().NotContain(RunId.ToString());
        ex.Message.Should().NotContainEquivalentOf("validators");
        ex.Missing.Select(m => m.ValidatorId).Should().Equal("hasRouting");
    }

    [Fact]
    public async Task EntityNotCreatedYet_UsesTheSamePlainMessage()
    {
        SeedRun(null);

        var act = () => _handler.Handle(new CompleteWorkflowRunCommand(RunId), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<WorkflowMissingValidatorsException>()).Which;
        ex.Message.Should().Be("Finish these before this part can be active: Basics.");
        ex.Message.Should().NotContain(RunId.ToString());
        ex.Message.Should().NotContainEquivalentOf("validators");
    }
}
