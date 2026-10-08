using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Capabilities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quality;

[Collection(CapabilityTestCollection.Name)]
public class QcTemplateEditTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 15, 30, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<QcTemplateResponseModel> CreateTemplateAsync() =>
        await new CreateQcTemplateHandler(_db).Handle(
            new CreateQcTemplateCommand(new CreateQcTemplateRequestModel(
                "Incoming",
                "Receiving check",
                null,
                [
                    new CreateQcTemplateItemModel("Thread gauge", "M8x1.25 go/no-go", 1, true),
                    new CreateQcTemplateItemModel("Packaging", null, 2, false),
                ])),
            CancellationToken.None);

    private Task<QcTemplateResponseModel> Update(int id, UpdateQcTemplateRequestModel data) =>
        new UpdateQcTemplateHandler(_db).Handle(new UpdateQcTemplateCommand(id, data), CancellationToken.None);

    private Task Delete(int id)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new DeleteQcTemplateHandler(_db, clock.Object).Handle(new DeleteQcTemplateCommand(id), CancellationToken.None);
    }

    private async Task<QcInspectionResponseModel> StartInspectionAsync(int templateId)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test")),
        });
        return await new CreateQcInspectionHandler(_db, accessor.Object).Handle(
            new CreateQcInspectionCommand(new CreateQcInspectionRequestModel(null, null, templateId, null, null)),
            CancellationToken.None);
    }

    private static UpdateQcTemplateItemModel Keep(QcTemplateItemModel item) =>
        new(item.Id, item.Description, item.Specification, item.SortOrder, item.IsRequired);

    [Fact]
    public async Task Creating_a_template_is_logged_on_the_template()
    {
        var template = await CreateTemplateAsync();

        _db.ActivityLogs.Should().ContainSingle(a =>
            a.EntityType == "QcTemplate" && a.EntityId == template.Id && a.Action == "created");
    }

    [Fact]
    public async Task Items_are_updated_added_and_removed_by_id_in_one_logged_change()
    {
        var template = await CreateTemplateAsync();
        var thread = template.Items.Single(i => i.Description == "Thread gauge");

        var result = await Update(template.Id, new UpdateQcTemplateRequestModel(
            "Incoming inspection",
            "Receiving check",
            null,
            [
                Keep(thread) with { Specification = "M8x1.25 6H" },
                new UpdateQcTemplateItemModel(null, "Hardness", "58-62 HRC", 3, true),
            ]));

        result.Name.Should().Be("Incoming inspection");
        result.Items.Select(i => i.Description).Should().Equal("Thread gauge", "Hardness");
        result.Items.Single(i => i.Description == "Thread gauge").Id.Should().Be(thread.Id);
        result.Items.Single(i => i.Description == "Thread gauge").Specification.Should().Be("M8x1.25 6H");
        var log = _db.ActivityLogs.Should().ContainSingle(a =>
            a.EntityType == "QcTemplate" && a.EntityId == template.Id && a.Action == "updated").Subject;
        log.Description.Should().Be("Updated 2 fields: name, items (1 added, 1 changed, 1 removed)");
    }

    [Fact]
    public async Task Resending_the_template_unchanged_logs_nothing()
    {
        var template = await CreateTemplateAsync();

        await Update(template.Id, new UpdateQcTemplateRequestModel(
            template.Name, template.Description, null, template.Items.Select(Keep).ToList()));

        _db.ActivityLogs.Should().NotContain(a => a.EntityType == "QcTemplate" && a.Action == "updated");
    }

    [Fact]
    public async Task Editing_a_template_leaves_past_inspections_unchanged()
    {
        var template = await CreateTemplateAsync();
        var inspection = await StartInspectionAsync(template.Id);
        var thread = template.Items.Single(i => i.Description == "Thread gauge");

        await Update(template.Id, new UpdateQcTemplateRequestModel(
            "Incoming v2",
            null,
            null,
            [Keep(thread) with { Description = "Thread pitch", Specification = "changed", IsRequired = false }]));

        var detail = await new GetQcInspectionByIdHandler(_db)
            .Handle(new GetQcInspectionByIdQuery(inspection.Id), CancellationToken.None);
        detail.Results.Select(r => (r.Description, r.Specification, r.IsRequired))
            .Should().Equal(("Thread gauge", "M8x1.25 go/no-go", true), ("Packaging", null, false));
        detail.Results.Single(r => r.Description == "Thread gauge").ChecklistItemId.Should().Be(thread.Id);
        detail.Results.Single(r => r.Description == "Packaging").ChecklistItemId.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_items_and_parts_are_field_errors()
    {
        var template = await CreateTemplateAsync();

        var act = () => Update(template.Id, new UpdateQcTemplateRequestModel(
            "Incoming", null, 888_888, [new UpdateQcTemplateItemModel(777_777, "Ghost", null, 1, true)]));

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.ToList();
        errors.Should().Contain(e => e.PropertyName == "partId" && e.ErrorMessage == "Pick a part that exists.");
        errors.Should().Contain(e => e.PropertyName == "items" && e.ErrorMessage.Contains("777777"));
    }

    [Fact]
    public async Task Updating_a_missing_template_is_not_found()
    {
        var act = () => Update(565_656, new UpdateQcTemplateRequestModel(
            "Nope", null, null, [new UpdateQcTemplateItemModel(null, "Item", null, 1, true)]));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void Validator_rejects_an_item_listed_twice()
    {
        var command = new UpdateQcTemplateCommand(1, new UpdateQcTemplateRequestModel(
            "Twice", null, null,
            [
                new UpdateQcTemplateItemModel(5, "A", null, 1, true),
                new UpdateQcTemplateItemModel(5, "B", null, 2, true),
            ]));

        var result = new UpdateQcTemplateCommandValidator().Validate(command);

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Each checklist item can appear only once.");
    }

    [Fact]
    public async Task Deleting_is_refused_while_an_inspection_using_the_template_is_in_progress()
    {
        var template = await CreateTemplateAsync();
        await StartInspectionAsync(template.Id);

        var act = () => Delete(template.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 inspection in progress*");
        (await _db.QcChecklistTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id)).DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deleting_soft_deletes_with_the_clock_and_keeps_completed_inspections()
    {
        var template = await CreateTemplateAsync();
        var inspection = await StartInspectionAsync(template.Id);
        var stored = await _db.QcInspections.SingleAsync(i => i.Id == inspection.Id);
        stored.Status = "Passed";
        await _db.SaveChangesAsync();

        await Delete(template.Id);

        var deleted = await _db.QcChecklistTemplates.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == template.Id);
        deleted.DeletedAt.Should().Be(Now);
        (await _db.QcChecklistTemplates.AnyAsync(t => t.Id == template.Id)).Should().BeFalse();
        _db.ActivityLogs.Should().ContainSingle(a =>
            a.EntityType == "QcTemplate" && a.EntityId == template.Id && a.Action == "deleted");
        var past = await new GetQcInspectionsHandler(_db)
            .Handle(new GetQcInspectionsQuery(null, null, null, null), CancellationToken.None);
        past.Single(i => i.Id == inspection.Id).TemplateName.Should().Be("Incoming");
        past.Single(i => i.Id == inspection.Id).Results.Should().HaveCount(2);
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task Only_admins_and_managers_can_edit_or_delete_templates()
    {
        var admin = Client("Admin");
        var worker = Client("ProductionWorker");
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = new QcChecklistTemplate { Name = $"HTTP {Guid.NewGuid():N}"[..20] };
            template.Items.Add(new QcChecklistItem { Description = "Look", SortOrder = 1 });
            db.QcChecklistTemplates.Add(template);
            await db.SaveChangesAsync();
            id = template.Id;
        }

        const string capability = "CAP-QC-INSPECTION";
        bool wasEnabled;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            wasEnabled = (await db.Capabilities.AsNoTracking().SingleAsync(c => c.Code == capability)).Enabled;
        }

        var body = new { name = "Renamed", items = new[] { new { description = "Look", sortOrder = 1, isRequired = true } } };
        HttpResponseMessage workerPut, workerDelete, adminPut, adminDelete;
        await admin.PutAsync($"/api/v1/capabilities/{capability}/enabled", JsonContent.Create(new { enabled = true }));
        try
        {
            workerPut = await worker.PutAsync($"/api/v1/quality/templates/{id}", JsonContent.Create(body));
            workerDelete = await worker.DeleteAsync($"/api/v1/quality/templates/{id}");
            adminPut = await admin.PutAsync($"/api/v1/quality/templates/{id}", JsonContent.Create(body));
            adminDelete = await admin.DeleteAsync($"/api/v1/quality/templates/{id}");
        }
        finally
        {
            if (!wasEnabled)
                await admin.PutAsync($"/api/v1/capabilities/{capability}/enabled", JsonContent.Create(new { enabled = false }));
        }

        workerPut.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        workerDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        adminPut.StatusCode.Should().Be(HttpStatusCode.OK);
        adminDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
