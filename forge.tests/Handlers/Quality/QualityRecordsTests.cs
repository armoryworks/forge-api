using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.Lots;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

/// <summary>
/// Quality records stay trustworthy: a completed inspection is locked.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class QualityRecordsTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly FixedClock _clock = new(Now);
    private readonly Mock<IMediator> _mediator = new();

    private UpdateQcInspectionHandler InspectionHandler()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test")),
        });
        return new UpdateQcInspectionHandler(_db, _mediator.Object, accessor.Object, _clock);
    }

    private async Task<QcInspection> SeedInspectionAsync(bool optionalSecondItem = false, int? jobId = null)
    {
        var template = new QcChecklistTemplate { Name = "Final" };
        template.Items.Add(new QcChecklistItem { Description = "Dimensions", SortOrder = 1, IsRequired = true });
        template.Items.Add(new QcChecklistItem { Description = "Finish", SortOrder = 2, IsRequired = !optionalSecondItem });
        _db.QcChecklistTemplates.Add(template);
        await _db.SaveChangesAsync();

        var inspection = new QcInspection { TemplateId = template.Id, InspectorId = 7, JobId = jobId, Status = "InProgress" };
        foreach (var item in template.Items)
            inspection.Results.Add(new QcInspectionResult { ChecklistItemId = item.Id, Description = item.Description });
        _db.QcInspections.Add(inspection);
        await _db.SaveChangesAsync();
        return inspection;
    }

    private static List<UpdateQcInspectionResultModel> Results(QcInspection inspection, params bool[] passed) =>
        inspection.Results.OrderBy(r => r.Id)
            .Select((r, i) => new UpdateQcInspectionResultModel(r.Id, r.ChecklistItemId, r.Description, passed[i], null, null))
            .ToList();

    private Task<QcInspectionResponseModel> Update(int id, string? status, List<UpdateQcInspectionResultModel>? results = null, string? notes = null) =>
        InspectionHandler().Handle(
            new UpdateQcInspectionCommand(id, new UpdateQcInspectionRequestModel(status, notes, results)),
            CancellationToken.None);

    [Fact]
    public async Task Results_are_updated_in_place_by_id()
    {
        var inspection = await SeedInspectionAsync();
        var originalIds = inspection.Results.Select(r => r.Id).OrderBy(id => id).ToList();

        var result = await Update(inspection.Id, null, Results(inspection, true, false));

        result.Results.Select(r => r.Id).Should().BeEquivalentTo(originalIds);
        result.Results.Single(r => r.Description == "Dimensions").Passed.Should().BeTrue();
        result.Status.Should().Be("InProgress");
        result.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Passed_is_rejected_when_a_required_checklist_item_failed()
    {
        var inspection = await SeedInspectionAsync();

        var act = () => Update(inspection.Id, "Passed", Results(inspection, true, false));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Finish*");
        (await _db.QcInspections.AsNoTracking().SingleAsync(i => i.Id == inspection.Id)).Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task Passed_is_allowed_when_only_an_optional_item_failed()
    {
        var inspection = await SeedInspectionAsync(optionalSecondItem: true);

        var result = await Update(inspection.Id, "Passed", Results(inspection, true, false));

        result.Status.Should().Be("Passed");
        result.CompletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_completed_inspection_rejects_further_updates()
    {
        var inspection = await SeedInspectionAsync();
        await Update(inspection.Id, "Passed", Results(inspection, true, true));
        _clock.UtcNow = Now.AddDays(1);

        var act = () => Update(inspection.Id, null, notes: "late edit");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Passed*");
        var stored = await _db.QcInspections.AsNoTracking().SingleAsync(i => i.Id == inspection.Id);
        stored.CompletedAt.Should().Be(Now);
        stored.Notes.Should().BeNull();
    }

    [Fact]
    public async Task Failing_publishes_the_failed_event_once_and_logs_activity()
    {
        var inspection = await SeedInspectionAsync(jobId: 42);

        await Update(inspection.Id, "Failed", Results(inspection, false, true));
        var again = () => Update(inspection.Id, "Failed");

        await again.Should().ThrowAsync<InvalidOperationException>();
        _mediator.Verify(m => m.Publish(It.IsAny<QcInspectionFailedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "QcInspection" && a.EntityId == inspection.Id && a.Action == "inspection-failed");
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "Job" && a.EntityId == 42);
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task WithCapabilityAsync(HttpClient admin, string capability, Func<Task> body)
    {
        bool wasEnabled;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            wasEnabled = (await db.Capabilities.AsNoTracking().SingleAsync(c => c.Code == capability)).Enabled;
        }

        await admin.PutAsync($"/api/v1/capabilities/{capability}/enabled", JsonContent.Create(new { enabled = true }));
        try
        {
            await body();
        }
        finally
        {
            if (!wasEnabled)
                await admin.PutAsync($"/api/v1/capabilities/{capability}/enabled", JsonContent.Create(new { enabled = false }));
        }
    }

    [Fact]
    public async Task Editing_a_passed_inspection_returns_409()
    {
        var admin = Client("Admin");
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inspection = new QcInspection { InspectorId = 1, Status = "Passed", CompletedAt = Now };
            db.QcInspections.Add(inspection);
            await db.SaveChangesAsync();
            id = inspection.Id;
        }

        HttpResponseMessage? response = null;
        await WithCapabilityAsync(admin, "CAP-QC-INSPECTION", async () =>
            response = await admin.PutAsync($"/api/v1/quality/inspections/{id}", JsonContent.Create(new { notes = "edit" })));

        response!.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
