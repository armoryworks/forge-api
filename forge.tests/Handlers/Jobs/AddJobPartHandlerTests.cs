using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Parts;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class AddJobPartHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly AddJobPartHandler _handler;

    public AddJobPartHandlerTests()
    {
        _handler = new AddJobPartHandler(_db);
    }

    private async Task<Job> SeedAsync(int? partId)
    {
        _db.Parts.AddRange(
            new Part { Id = 800, PartNumber = "40-1700M", Description = "Clutch weight", CurrentBomRevisionId = 21 },
            new Part { Id = 801, PartNumber = "40-1800M", Description = "Spacer" });
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test", TrackTypeId = 1, CurrentStageId = 1, PartId = partId };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task The_first_part_on_a_partless_job_becomes_the_job_part()
    {
        var job = await SeedAsync(partId: null);

        await _handler.Handle(new AddJobPartCommand(job.Id, 800, 12m), CancellationToken.None);

        job.PartId.Should().Be(800);
        job.BomRevisionIdAtRelease.Should().Be(21);
        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == job.Id);
        log.FieldName.Should().Be("Part");
        log.NewValue.Should().Be("40-1700M");
    }

    [Fact]
    public async Task A_job_that_already_has_a_part_keeps_it()
    {
        var job = await SeedAsync(partId: 800);

        await _handler.Handle(new AddJobPartCommand(job.Id, 801, 3m), CancellationToken.None);

        job.PartId.Should().Be(800);
        job.BomRevisionIdAtRelease.Should().BeNull();
        (await _db.JobActivityLogs.CountAsync(l => l.JobId == job.Id)).Should().Be(1);
    }
}
