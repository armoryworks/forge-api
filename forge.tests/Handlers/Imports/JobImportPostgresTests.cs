using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Imports;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Imports;

[Collection(PostgresCollection.Name)]
public sealed class JobImportPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_blank_number_draws_past_the_numbers_in_the_same_file()
    {
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = $"Import {suffix}", Code = $"imp_{suffix}", IsActive = true, SortOrder = 999 };
        track.Stages.Add(new JobStage { Name = "Queued", Code = $"q_{suffix}", SortOrder = 1, IsActive = true });
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();

        var next = await db.Database
            .SqlQueryRaw<int>("SELECT CAST(nextval('job_number_seq') AS int) AS \"Value\"")
            .SingleAsync();
        var fileNumber = $"J-{next + 1}";
        JobImportRowModel Row(string? number, string title) =>
            new($"r-{title}", number, title, null, null, null, null, null, null, null, track.Name, null, null, null);

        var result = await ImportJobsTests.Handler(db).Handle(new ImportJobsCommand(
            [Row(null, $"Blank {suffix}"), Row(fileNumber, $"Numbered {suffix}")], Commit: true), CancellationToken.None);

        result.Committed.Should().BeTrue();
        await using var check = fixture.CreateContext();
        var drawn = await check.Jobs.Where(j => j.Title == $"Blank {suffix}").Select(j => j.JobNumber).SingleAsync();
        drawn.Should().NotBe(fileNumber);
        int.Parse(drawn[2..]).Should().BeGreaterThan(next + 1);
        (await check.Jobs.AnyAsync(j => j.JobNumber == fileNumber)).Should().BeTrue();
    }
}
