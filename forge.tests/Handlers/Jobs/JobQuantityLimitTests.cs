using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Jobs.Parts;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class JobQuantityLimitTests
{
    [Theory]
    [InlineData(100_000_000, JobQuantityRules.TooLargeMessage)]
    [InlineData(1.234, JobQuantityRules.ScaleMessage)]
    public void Job_quantities_must_fit_the_stored_precision(double quantity, string message)
    {
        var value = (decimal)quantity;

        new CreateJobCommandValidator().Validate(new CreateJobCommand("T", null, 1, null, null, null, null, PartId: 1, Quantity: value))
            .Errors.Should().Contain(e => e.ErrorMessage == message);
        new UpdateJobPartValidator().Validate(new UpdateJobPartCommand(1, 1, value, null))
            .Errors.Should().Contain(e => e.ErrorMessage == message);
        new AddJobPartValidator().Validate(new AddJobPartCommand(1, 1, value))
            .Errors.Should().Contain(e => e.ErrorMessage == message);
    }

    [Fact]
    public void The_largest_stored_quantity_is_accepted()
    {
        new UpdateJobPartValidator().Validate(new UpdateJobPartCommand(1, 1, JobQuantityRules.Max, null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_defaulted_quantity_is_rounded_up_to_the_stored_scale()
    {
        JobQuantityRules.Fit(2.3333m).Should().Be(2.34m);
        JobQuantityRules.Fit(5m).Should().Be(5m);
    }

    private static async Task<(Forge.Data.Context.AppDbContext Db, Job Job, JobPart Build, JobPart Other)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var build = new Part { PartNumber = "BUILD", Name = "Build" };
        var other = new Part { PartNumber = "OTHER", Name = "Other" };
        db.Parts.AddRange(build, other);
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = "J-PARTS", Title = "Parts", PartId = build.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var buildRow = new JobPart { JobId = job.Id, PartId = build.Id, Quantity = 5m };
        var otherRow = new JobPart { JobId = job.Id, PartId = other.Id, Quantity = 2m };
        db.JobParts.AddRange(buildRow, otherRow);
        await db.SaveChangesAsync();
        return (db, job, buildRow, otherRow);
    }

    [Fact]
    public async Task Only_a_manager_can_remove_the_part_the_work_order_builds()
    {
        var (db, job, build, other) = await SeedAsync();
        await using var _ = db;

        var asWorker = () => new RemoveJobPartHandler(db, TestCaller.As(3, "ProductionWorker"))
            .Handle(new RemoveJobPartCommand(job.Id, build.Id), default);
        await asWorker.Should().ThrowAsync<ForbiddenException>();

        await new RemoveJobPartHandler(db, TestCaller.As(3, "ProductionWorker"))
            .Handle(new RemoveJobPartCommand(job.Id, other.Id), default);
        await new RemoveJobPartHandler(db, TestCaller.As(4, "Manager"))
            .Handle(new RemoveJobPartCommand(job.Id, build.Id), default);

        (await db.JobParts.CountAsync()).Should().Be(0);
    }
}
