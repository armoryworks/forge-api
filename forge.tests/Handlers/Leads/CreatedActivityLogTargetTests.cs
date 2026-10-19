using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.AssignmentRules;
using Forge.Api.Features.IcpRubrics;
using Forge.Api.Features.LeadSources;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

/// <summary>
/// The lead-setup create handlers used to write their "created" activity row against entity id 0,
/// so it never showed in the new record's history. The row must point at the saved record.
/// </summary>
public class CreatedActivityLogTargetTests
{
    [Fact]
    public async Task Assignment_rule_created_activity_points_at_the_rule()
    {
        await using var db = TestDbContextFactory.Create();
        var result = await new CreateAssignmentRuleHandler(db).Handle(
            new CreateAssignmentRuleCommand(new CreateAssignmentRuleRequest("West", AssignmentRuleKind.RoundRobin, 1, null)),
            CancellationToken.None);

        var log = await db.ActivityLogs.SingleAsync(l => l.Action == "assignment-rule-created");
        log.EntityType.Should().Be("AssignmentRule");
        log.EntityId.Should().Be(result.Id).And.NotBe(0);
    }

    [Fact]
    public async Task Icp_rubric_created_activity_points_at_the_rubric()
    {
        await using var db = TestDbContextFactory.Create();
        var result = await new CreateIcpRubricHandler(db).Handle(
            new CreateIcpRubricCommand(new CreateIcpRubricRequest("Mid-market", null)), CancellationToken.None);

        var log = await db.ActivityLogs.SingleAsync(l => l.Action == "icp-rubric-created");
        log.EntityId.Should().Be(result.Id).And.NotBe(0);
    }

    [Fact]
    public async Task Lead_source_created_activity_points_at_the_source()
    {
        await using var db = TestDbContextFactory.Create();
        var result = await new CreateLeadSourceHandler(db).Handle(
            new CreateLeadSourceCommand(new CreateLeadSourceRequest("Trade show", "TRADESHOW", null)), CancellationToken.None);

        var log = await db.ActivityLogs.SingleAsync(l => l.Action == "lead-source-created");
        log.EntityId.Should().Be(result.Id).And.NotBe(0);
    }
}
