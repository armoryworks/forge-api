using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using MediatR;
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
public class QcInspectionLinksTests(CapabilityTestWebApplicationFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private static IHttpContextAccessor Accessor(int userId = 7)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
        });
        return accessor.Object;
    }

    private Task<QcInspectionResponseModel> Create(CreateQcInspectionRequestModel data) =>
        new CreateQcInspectionHandler(_db, Accessor()).Handle(new CreateQcInspectionCommand(data), CancellationToken.None);

    private Task<QcInspectionResponseModel> Update(int id, string? status, List<UpdateQcInspectionResultModel>? results = null)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new UpdateQcInspectionHandler(_db, new Mock<IMediator>().Object, Accessor(), clock.Object).Handle(
            new UpdateQcInspectionCommand(id, new UpdateQcInspectionRequestModel(status, null, results)),
            CancellationToken.None);
    }

    private Task<List<QcInspectionResponseModel>> Search(string? search, string? status = null) =>
        new GetQcInspectionsHandler(_db).Handle(
            new GetQcInspectionsQuery(null, status, null, search), CancellationToken.None);

    private async Task<Part> SeedPartAsync(string partNumber)
    {
        var part = new Part { PartNumber = partNumber, Name = $"{partNumber} name" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Job> SeedJobAsync(string jobNumber, int? partId = null)
    {
        var job = new Job { JobNumber = jobNumber, Title = $"{jobNumber} title", PartId = partId };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<QcChecklistTemplate> SeedTemplateAsync()
    {
        var template = new QcChecklistTemplate { Name = "Final audit" };
        template.Items.Add(new QcChecklistItem { Description = "Bore", Specification = "12.00 +/- 0.05 mm", SortOrder = 1, IsRequired = true });
        template.Items.Add(new QcChecklistItem { Description = "Cosmetic", SortOrder = 2, IsRequired = false });
        _db.QcChecklistTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    private static List<UpdateQcInspectionResultModel> Results(QcInspectionResponseModel inspection, params bool[] passed) =>
        inspection.Results
            .Select((r, i) => new UpdateQcInspectionResultModel(r.Id, r.ChecklistItemId, r.Description, passed[i], null, null))
            .ToList();

    [Fact]
    public async Task Zero_ids_are_treated_as_no_link()
    {
        var result = await Create(new CreateQcInspectionRequestModel(0, 0, 0, "LOT-Z", null, 0));

        result.JobId.Should().BeNull();
        result.ProductionRunId.Should().BeNull();
        result.TemplateId.Should().BeNull();
        result.PartId.Should().BeNull();
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Links_that_point_at_nothing_are_field_errors()
    {
        var act = () => Create(new CreateQcInspectionRequestModel(999_001, 999_002, 999_003, null, null, 999_004));

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.ToList();
        errors.Should().ContainSingle(e => e.PropertyName == "jobId" && e.ErrorMessage == "Pick a work order that exists.");
        errors.Should().ContainSingle(e => e.PropertyName == "productionRunId" && e.ErrorMessage == "Pick a production run that exists.");
        errors.Should().ContainSingle(e => e.PropertyName == "templateId" && e.ErrorMessage == "Pick a checklist template that exists.");
        errors.Should().ContainSingle(e => e.PropertyName == "partId" && e.ErrorMessage == "Pick a part that exists.");
        (await _db.QcInspections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_deleted_or_inactive_template_cannot_start_an_inspection()
    {
        var deleted = new QcChecklistTemplate { Name = "Gone", DeletedAt = Now };
        var inactive = new QcChecklistTemplate { Name = "Retired", IsActive = false };
        _db.QcChecklistTemplates.AddRange(deleted, inactive);
        await _db.SaveChangesAsync();

        foreach (var id in new[] { deleted.Id, inactive.Id })
        {
            var act = () => Create(new CreateQcInspectionRequestModel(null, null, id, null, null));
            (await act.Should().ThrowAsync<ValidationException>())
                .Which.Errors.Should().ContainSingle(e => e.PropertyName == "templateId");
        }
    }

    [Fact]
    public async Task The_part_defaults_to_the_jobs_part_and_an_explicit_part_wins()
    {
        var jobPart = await SeedPartAsync("P-JOB");
        var otherPart = await SeedPartAsync("P-OTHER");
        var job = await SeedJobAsync("WO-100", jobPart.Id);

        var defaulted = await Create(new CreateQcInspectionRequestModel(job.Id, null, null, null, null));
        var explicitPart = await Create(new CreateQcInspectionRequestModel(job.Id, null, null, null, null, otherPart.Id));

        defaulted.PartId.Should().Be(jobPart.Id);
        defaulted.PartNumber.Should().Be("P-JOB");
        defaulted.JobNumber.Should().Be("WO-100");
        explicitPart.PartId.Should().Be(otherPart.Id);
    }

    [Fact]
    public async Task Results_snapshot_the_template_criteria_and_creation_is_logged_on_inspection_and_job()
    {
        var template = await SeedTemplateAsync();
        var job = await SeedJobAsync("WO-200");

        var result = await Create(new CreateQcInspectionRequestModel(job.Id, null, template.Id, null, null));

        result.Results.Should().SatisfyRespectively(
            bore =>
            {
                bore.Description.Should().Be("Bore");
                bore.Specification.Should().Be("12.00 +/- 0.05 mm");
                bore.IsRequired.Should().BeTrue();
            },
            cosmetic =>
            {
                cosmetic.Description.Should().Be("Cosmetic");
                cosmetic.Specification.Should().BeNull();
                cosmetic.IsRequired.Should().BeFalse();
            });
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "QcInspection" && a.EntityId == result.Id && a.Action == "inspection-started");
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityType == "Job" && a.EntityId == job.Id && a.Action == "inspection-started");
    }

    [Fact]
    public async Task Passing_reads_required_from_the_snapshot_not_the_current_template()
    {
        var template = await SeedTemplateAsync();
        var inspection = await Create(new CreateQcInspectionRequestModel(null, null, template.Id, null, null));
        foreach (var item in await _db.QcChecklistItems.ToListAsync())
            item.IsRequired = item.Description == "Cosmetic";
        await _db.SaveChangesAsync();

        var refused = () => Update(inspection.Id, "Passed", Results(inspection, false, true));
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Bore*");

        var passed = await Update(inspection.Id, "Passed", Results(inspection, true, false));
        passed.Status.Should().Be("Passed");
    }

    [Fact]
    public async Task A_required_result_cannot_be_dropped_to_dodge_the_pass_check()
    {
        var template = await SeedTemplateAsync();
        var inspection = await Create(new CreateQcInspectionRequestModel(null, null, template.Id, null, null));
        var withoutBore = Results(inspection, false, true).Where(r => r.Description != "Bore").ToList();

        var act = () => Update(inspection.Id, "Passed", withoutBore);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "Results" && e.ErrorMessage.Contains("Bore"));
    }

    [Fact]
    public async Task An_ad_hoc_result_added_during_inspection_is_optional()
    {
        var inspection = await Create(new CreateQcInspectionRequestModel(null, null, null, null, null));

        var updated = await Update(inspection.Id, null,
            [new UpdateQcInspectionResultModel(null, null, "Burr check", false, null, null)]);
        var passed = await Update(inspection.Id, "Passed", Results(updated, false));

        updated.Results.Should().ContainSingle().Which.IsRequired.Should().BeFalse();
        passed.Status.Should().Be("Passed");
    }

    [Fact]
    public async Task Detail_returns_the_inspection_with_its_names_and_results()
    {
        var part = await SeedPartAsync("P-DET");
        var job = await SeedJobAsync("WO-300", part.Id);
        var template = await SeedTemplateAsync();
        var run = new ProductionRun { JobId = job.Id, PartId = part.Id, RunNumber = "RUN-300" };
        _db.ProductionRuns.Add(run);
        _db.Users.Add(new ApplicationUser { Id = 7, FirstName = "Ada", LastName = "Inspector", UserName = "ada", Email = "ada@example.test" });
        await _db.SaveChangesAsync();
        var created = await Create(new CreateQcInspectionRequestModel(job.Id, run.Id, template.Id, "LOT-300", "first article"));

        var detail = await new GetQcInspectionByIdHandler(_db)
            .Handle(new GetQcInspectionByIdQuery(created.Id), CancellationToken.None);

        detail.JobNumber.Should().Be("WO-300");
        detail.JobTitle.Should().Be("WO-300 title");
        detail.ProductionRunNumber.Should().Be("RUN-300");
        detail.TemplateName.Should().Be("Final audit");
        detail.PartNumber.Should().Be("P-DET");
        detail.PartName.Should().Be("P-DET name");
        detail.LotNumber.Should().Be("LOT-300");
        detail.InspectorName.Should().Be("Inspector, Ada");
        detail.Results.Select(r => r.Description).Should().Equal("Bore", "Cosmetic");
    }

    [Fact]
    public async Task Detail_of_an_unknown_inspection_is_not_found()
    {
        var act = () => new GetQcInspectionByIdHandler(_db)
            .Handle(new GetQcInspectionByIdQuery(424_242), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Detail_keeps_the_template_name_after_the_template_is_deleted()
    {
        var template = await SeedTemplateAsync();
        var created = await Create(new CreateQcInspectionRequestModel(null, null, template.Id, null, null));
        template.DeletedAt = Now;
        await _db.SaveChangesAsync();

        var detail = await new GetQcInspectionByIdHandler(_db)
            .Handle(new GetQcInspectionByIdQuery(created.Id), CancellationToken.None);

        detail.TemplateName.Should().Be("Final audit");
    }

    [Fact]
    public async Task Search_matches_job_number_lot_and_part_number()
    {
        var part = await SeedPartAsync("BRKT-77");
        var job = await SeedJobAsync("WO-9001", part.Id);
        var legacyJob = await SeedJobAsync("WO-9002", part.Id);
        var byJob = await Create(new CreateQcInspectionRequestModel(job.Id, null, null, null, null));
        var byLot = await Create(new CreateQcInspectionRequestModel(null, null, null, "HEAT-5521", null));
        var legacy = new QcInspection { JobId = legacyJob.Id, InspectorId = 7, Status = "InProgress" };
        _db.QcInspections.Add(legacy);
        await _db.SaveChangesAsync();

        (await Search("wo-9001")).Select(i => i.Id).Should().Equal(byJob.Id);
        (await Search("heat-55")).Select(i => i.Id).Should().Equal(byLot.Id);
        (await Search("brkt")).Select(i => i.Id).Should().BeEquivalentTo([byJob.Id, legacy.Id]);
    }

    [Fact]
    public async Task Status_filter_accepts_several_statuses()
    {
        _db.QcInspections.AddRange(
            new QcInspection { InspectorId = 7, Status = "InProgress" },
            new QcInspection { InspectorId = 7, Status = "Failed" },
            new QcInspection { InspectorId = 7, Status = "Passed" });
        await _db.SaveChangesAsync();

        (await Search(null, "InProgress, Failed")).Select(i => i.Status).Should().BeEquivalentTo(["InProgress", "Failed"]);
        (await Search(null, "Passed")).Select(i => i.Status).Should().Equal("Passed");
    }

    private HttpClient Client(string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task WithInspectionCapabilityAsync(HttpClient admin, Func<Task> body)
    {
        const string capability = "CAP-QC-INSPECTION";
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
    public async Task Creating_with_an_unknown_job_returns_400_with_a_field_message_and_job_0_is_accepted()
    {
        var admin = Client("Admin");
        HttpResponseMessage? unknown = null, zero = null;

        await WithInspectionCapabilityAsync(admin, async () =>
        {
            unknown = await admin.PostAsync("/api/v1/quality/inspections", JsonContent.Create(new { jobId = 987_654 }));
            zero = await admin.PostAsync("/api/v1/quality/inspections", JsonContent.Create(new { jobId = 0, lotNumber = "LOT-ZERO" }));
        });

        unknown!.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        var error = body.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("field").GetString().Should().Be("jobId");
        error.GetProperty("message").GetString().Should().Be("Pick a work order that exists.");

        zero!.StatusCode.Should().Be(HttpStatusCode.Created);
        (await zero.Content.ReadFromJsonAsync<QcInspectionResponseModel>())!.JobId.Should().BeNull();
    }

    [Fact]
    public async Task An_inspection_can_be_read_by_id_over_http()
    {
        var admin = Client("Admin");
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inspection = new QcInspection { InspectorId = 1, Status = "InProgress", LotNumber = "LOT-HTTP" };
            db.QcInspections.Add(inspection);
            await db.SaveChangesAsync();
            id = inspection.Id;
        }

        HttpResponseMessage? found = null, missing = null;
        await WithInspectionCapabilityAsync(admin, async () =>
        {
            found = await admin.GetAsync($"/api/v1/quality/inspections/{id}");
            missing = await admin.GetAsync("/api/v1/quality/inspections/999999");
        });

        found!.StatusCode.Should().Be(HttpStatusCode.OK);
        (await found.Content.ReadFromJsonAsync<QcInspectionDetailResponseModel>())!.LotNumber.Should().Be("LOT-HTTP");
        missing!.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
