using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Projects;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Projects;

/// <summary>Linking a job to a WBS element is stored, so the project lists its jobs; linking twice keeps one link and unlinking removes it.</summary>
public class ProjectJobLinkTests
{
    private static IHttpContextAccessor Http() => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "4")])),
        },
    };

    private static async Task<(Project Project, WbsElement Element, Job Job)> SeedAsync(AppDbContext db)
    {
        var track = new TrackType { Name = "Production", Code = "prod" };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Build", Code = "build", Color = "#123456" };
        var project = new Project { ProjectNumber = "PRJ-0001", Name = "Line upgrade" };
        db.AddRange(stage, project);
        await db.SaveChangesAsync();
        var element = new WbsElement { ProjectId = project.Id, Code = "1.1", Name = "Fixtures" };
        var job = new Job { JobNumber = "J-1", Title = "Fixture plate", TrackTypeId = track.Id, CurrentStageId = stage.Id };
        db.AddRange(element, job);
        await db.SaveChangesAsync();
        return (project, element, job);
    }

    [Fact]
    public async Task A_linked_job_is_listed_on_the_project_once()
    {
        using var db = TestDbContextFactory.Create();
        var (project, element, job) = await SeedAsync(db);
        var link = new LinkJobToWbsHandler(db, new SystemClock(), Http());

        await link.Handle(new LinkJobToWbsCommand(project.Id, element.Id, new LinkJobToWbsRequestModel { JobId = job.Id }), CancellationToken.None);
        await link.Handle(new LinkJobToWbsCommand(project.Id, element.Id, new LinkJobToWbsRequestModel { JobId = job.Id }), CancellationToken.None);

        var jobs = await new GetProjectJobsHandler(db).Handle(new GetProjectJobsQuery(project.Id), CancellationToken.None);
        var row = jobs.Should().ContainSingle().Subject;
        row.JobNumber.Should().Be("J-1");
        row.WbsCode.Should().Be("1.1");
        row.StageName.Should().Be("Build");
        db.WbsElementJobs.Single().LinkedById.Should().Be(4);
    }

    [Fact]
    public async Task Unlinking_removes_the_job_from_the_project()
    {
        using var db = TestDbContextFactory.Create();
        var (project, element, job) = await SeedAsync(db);
        await new LinkJobToWbsHandler(db, new SystemClock(), Http())
            .Handle(new LinkJobToWbsCommand(project.Id, element.Id, new LinkJobToWbsRequestModel { JobId = job.Id }), CancellationToken.None);

        await new UnlinkJobFromWbsHandler(db).Handle(new UnlinkJobFromWbsCommand(project.Id, element.Id, job.Id), CancellationToken.None);

        (await new GetProjectJobsHandler(db).Handle(new GetProjectJobsQuery(project.Id), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_element_of_another_project_is_not_found()
    {
        using var db = TestDbContextFactory.Create();
        var (_, element, job) = await SeedAsync(db);

        var act = () => new LinkJobToWbsHandler(db, new SystemClock(), Http())
            .Handle(new LinkJobToWbsCommand(999, element.Id, new LinkJobToWbsRequestModel { JobId = job.Id }), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
