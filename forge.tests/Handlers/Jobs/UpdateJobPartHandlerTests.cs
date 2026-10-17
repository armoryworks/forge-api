using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class UpdateJobPartHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateJobPartHandler _handler;

    public UpdateJobPartHandlerTests()
    {
        _handler = new UpdateJobPartHandler(_db);
    }

    private async Task<Job> SeedAsync()
    {
        _db.Parts.AddRange(
            new Part { Id = 800, PartNumber = "40-1700M", Description = "Clutch weight" },
            new Part { Id = 801, PartNumber = "40-1800M", Description = "Spacer" });
        var job = new Job { Id = 1, JobNumber = "J-1", Title = "Test", TrackTypeId = 1, CurrentStageId = 1, PartId = 800 };
        job.JobParts.Add(new JobPart { Id = 10, PartId = 800, Quantity = 1m });
        job.JobParts.Add(new JobPart { Id = 11, PartId = 801, Quantity = 2m });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Changing_the_build_parts_quantity_is_logged_as_the_job_quantity()
    {
        await SeedAsync();
        _db.CurrentUserId = 17;

        await _handler.Handle(new UpdateJobPartCommand(1, 10, 300m, null), CancellationToken.None);

        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == 1);
        log.Action.Should().Be(ActivityAction.FieldChanged);
        log.FieldName.Should().Be("Quantity");
        log.OldValue.Should().Be("1");
        log.NewValue.Should().Be("300");
        log.UserId.Should().Be(17);
    }

    [Fact]
    public async Task Changing_another_parts_quantity_names_that_part()
    {
        await SeedAsync();

        await _handler.Handle(new UpdateJobPartCommand(1, 11, 5m, null), CancellationToken.None);

        var log = await _db.JobActivityLogs.SingleAsync(l => l.JobId == 1);
        log.FieldName.Should().BeNull();
        log.Description.Should().Contain("40-1800M");
    }

    [Fact]
    public async Task A_notes_only_edit_logs_nothing()
    {
        await SeedAsync();

        await _handler.Handle(new UpdateJobPartCommand(1, 10, 1m, "Rush"), CancellationToken.None);

        (await _db.JobActivityLogs.CountAsync()).Should().Be(0);
    }
}
