using FluentAssertions;

using Forge.Api.Features.Reports;
using Forge.Api.Features.Terminology;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Terminology;

public class ServerTermTextTests
{
    [Fact]
    public async Task ReportBuilderLabels_FollowTheInstallTermsAndKeepTheirKeys()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry { Key = "entity_job", Label = "Work Order" });
        await db.SaveChangesAsync();

        var entities = await new GetReportEntitiesHandler(new TerminologyLabels(db))
            .Handle(new GetReportEntitiesQuery(), CancellationToken.None);

        var jobs = entities.Single(e => e.EntitySource == "Jobs");
        jobs.Label.Should().Be("Work Orders");
        jobs.Fields.Single(f => f.Field == "JobNumber").Label.Should().Be("Work Order Number");
        jobs.Fields.Single(f => f.Field == "ParentJob.JobNumber").Label.Should().Be("Parent Work Order Number");
        entities.SelectMany(e => e.Fields).Should().NotContain(f => f.Label.Contains("Job"));
    }

    [Fact]
    public void DigestEmail_UsesTheInstallTermForHeadersAndCounts()
    {
        var terms = new TermLabels(new Dictionary<string, string> { ["entity_job"] = "Work Order" });
        var overdue = new List<DigestJobItem> { new("J-4", "Bracket run", DateTimeOffset.UtcNow.AddDays(-1)) };

        var html = EmailTemplateBuilder.BuildDigest("Acme", "Sam", [], overdue, 1, terms);

        html.Should().Contain("Overdue Work Orders").And.Contain("Work Order #").And.Contain("1 work order completed yesterday");
        html.Should().NotContain("Job #").And.NotContain("Overdue Jobs");
    }
}
