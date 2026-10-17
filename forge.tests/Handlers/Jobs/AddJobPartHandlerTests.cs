using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
    public async Task The_activity_row_credits_the_current_user()
    {
        var job = await SeedAsync(partId: null);
        _db.CurrentUserId = 17;

        await _handler.Handle(new AddJobPartCommand(job.Id, 800, 1m), CancellationToken.None);

        (await _db.JobActivityLogs.SingleAsync(l => l.JobId == job.Id)).UserId.Should().Be(17);
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

    private async Task<SalesOrderLine> SeedLineAsync(int? partId, decimal quantity)
    {
        var so = new SalesOrder { OrderNumber = "SO-1", CustomerId = 42, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id, PartId = partId, Description = "Clutch weights",
            Quantity = quantity, UnitPrice = 3m, LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    [Fact]
    public async Task The_first_part_on_a_line_linked_job_defaults_to_the_lines_quantity()
    {
        var job = await SeedAsync(partId: null);
        var line = await SeedLineAsync(partId: null, quantity: 300m);
        job.SalesOrderLineId = line.Id;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new AddJobPartCommand(job.Id, 800), CancellationToken.None);

        result.Quantity.Should().Be(300m);
        (await _db.JobActivityLogs.SingleAsync(l => l.JobId == job.Id)).Description.Should().Contain("qty 300");
    }

    [Fact]
    public async Task A_caller_supplied_quantity_wins_over_the_lines()
    {
        var job = await SeedAsync(partId: null);
        var line = await SeedLineAsync(partId: 800, quantity: 300m);
        job.SalesOrderLineId = line.Id;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new AddJobPartCommand(job.Id, 800, 40m), CancellationToken.None);

        result.Quantity.Should().Be(40m);
    }

    [Fact]
    public async Task An_extra_part_without_a_quantity_defaults_to_one()
    {
        var job = await SeedAsync(partId: 800);
        var line = await SeedLineAsync(partId: 800, quantity: 300m);
        job.SalesOrderLineId = line.Id;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new AddJobPartCommand(job.Id, 801), CancellationToken.None);

        result.Quantity.Should().Be(1m);
    }

    [Fact]
    public void The_validator_rejects_a_quantity_that_is_not_positive()
    {
        new AddJobPartValidator().Validate(new AddJobPartCommand(1, 800, 0m)).IsValid.Should().BeFalse();
        new AddJobPartValidator().Validate(new AddJobPartCommand(1, 800)).IsValid.Should().BeTrue();
    }
}
