using System.Net;
using System.Net.Http.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

[Collection(CapabilityTestCollection.Name)]
public class WorkflowDefinitionsApiTests(CapabilityTestWebApplicationFactory factory)
{
    private HttpClient AuthenticatedClient(string role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task List_ReturnsSeededDefinitions()
    {
        var client = AuthenticatedClient();
        var rows = await client.GetFromJsonAsync<List<WorkflowDefinitionResponseModel>>(
            "/api/v1/workflow-definitions?entityType=Part");
        rows!.Select(r => r.DefinitionId).Should().Contain(
            ["part-make-subassembly-v1", "part-buy-raw-v1"]);
    }

    [Fact]
    public async Task Get_PinnedSeed_Returns200()
    {
        var client = AuthenticatedClient();
        var resp = await client.GetFromJsonAsync<WorkflowDefinitionResponseModel>(
            "/api/v1/workflow-definitions/part-make-subassembly-v1");
        resp!.EntityType.Should().Be("Part");
        resp!.DefaultMode.Should().Be("guided");
    }

    [Fact]
    public async Task Update_Admin_AppliesNewSteps()
    {
        var client = AuthenticatedClient();

        // Snapshot the seed StepsJson so we can restore on cleanup —
        // the Capabilities test collection runs sequentially and other
        // workflow lifecycle tests assume the seed shape.
        var original = await client.GetFromJsonAsync<WorkflowDefinitionResponseModel>(
            "/api/v1/workflow-definitions/part-make-subassembly-v1");
        try
        {
            var body = new UpsertWorkflowDefinitionRequestModel(
                "part-make-subassembly-v1", "Part", "guided",
                """[{"id":"basics","labelKey":"k","componentName":"c","required":true,"completionGates":["hasBasics"]}]""",
                "PartExpressFormComponent");
            var response = await client.PutAsJsonAsync(
                "/api/v1/workflow-definitions/part-make-subassembly-v1", body);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await response.Content.ReadFromJsonAsync<WorkflowDefinitionResponseModel>();
            updated!.StepsJson.Should().Contain("basics");
        }
        finally
        {
            await client.PutAsJsonAsync(
                "/api/v1/workflow-definitions/part-make-subassembly-v1",
                new UpsertWorkflowDefinitionRequestModel(
                    original!.DefinitionId, original.EntityType, original.DefaultMode,
                    original.StepsJson, original.ExpressTemplateComponent));
        }
    }

    [Fact]
    public async Task Delete_RejectsWhenInFlightRunsExist()
    {
        // Spin up a fake run by writing directly so the test isn't dependent
        // on the workflow-start path's other moving parts.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.WorkflowRuns.Add(new WorkflowRun
            {
                EntityType = "Part",
                EntityId = 999_001,
                DefinitionId = "part-buy-raw-v1",
                CurrentStepId = "basics",
                Mode = "express",
                StartedAt = DateTimeOffset.UtcNow,
                StartedByUserId = 1,
                LastActivityAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var client = AuthenticatedClient();
        var response = await client.DeleteAsync("/api/v1/workflow-definitions/part-buy-raw-v1");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Cleanup so other tests in the collection are unaffected.
        using var cleanupScope = factory.Services.CreateScope();
        var ctx = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orphaned = await ctx.WorkflowRuns
            .Where(r => r.EntityId == 999_001)
            .ToListAsync();
        ctx.WorkflowRuns.RemoveRange(orphaned);
        await ctx.SaveChangesAsync();
    }
}
