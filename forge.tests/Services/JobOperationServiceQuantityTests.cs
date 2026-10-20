using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class JobOperationServiceQuantityTests
{
    [Fact]
    public async Task A_job_without_a_part_row_takes_its_order_line_quantity()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "BRKT", Name = "Bracket" };
        var customer = new Customer { Name = "Acme" };
        db.AddRange(part, customer);
        await db.SaveChangesAsync();
        var so = new SalesOrder { OrderNumber = "SO-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Quantity = 300m, UnitPrice = 1m, LineNumber = 1 };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = "J-1", Title = "Brackets", PartId = part.Id, SalesOrderLineId = line.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var quantity = await new JobOperationService(db, Mock.Of<ISettingsService>(), Mock.Of<IClock>())
            .GetJobQuantityAsync(job, default);

        quantity.Should().Be(300m);
    }

    [Fact]
    public async Task The_part_row_quantity_wins_over_the_order_line()
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "BRKT", Name = "Bracket" };
        var customer = new Customer { Name = "Acme" };
        db.AddRange(part, customer);
        await db.SaveChangesAsync();
        var so = new SalesOrder { OrderNumber = "SO-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Quantity = 300m, UnitPrice = 1m, LineNumber = 1 };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = "J-1", Title = "Brackets", PartId = part.Id, SalesOrderLineId = line.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 315m });
        await db.SaveChangesAsync();

        var quantity = await new JobOperationService(db, Mock.Of<ISettingsService>(), Mock.Of<IClock>())
            .GetJobQuantityAsync(job, default);

        quantity.Should().Be(315m);
    }

    private static async Task<(Forge.Data.Context.AppDbContext Db, Job Job, Operation First, Operation Last)> SeedPlannedJobAsync(
        decimal? target, decimal start)
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "CAP", Name = "Cap" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var first = new Operation { PartId = part.Id, StepNumber = 10, Title = "Mold", ScrapFactor = 0.02m };
        var last = new Operation { PartId = part.Id, StepNumber = 20, Title = "Trim", ScrapFactor = 0.05m };
        db.Operations.AddRange(first, last);
        var job = new Job { JobNumber = "J-2", Title = "Caps", PartId = part.Id, TargetQuantity = target };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = start });
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return (db, job, first, last);
    }

    private static JobOperationService Service(Forge.Data.Context.AppDbContext db) =>
        new(db, Mock.Of<ISettingsService>(), Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task Each_step_shows_its_planned_quantity_backwards_from_the_target()
    {
        var (db, job, _, _) = await SeedPlannedJobAsync(300m, 322m);

        var built = await Service(db).BuildAsync(job.Id, default);

        built.JobQuantity.Should().Be(322m);
        built.TargetQuantity.Should().Be(300m);
        built.Operations.Select(o => o.PlannedQuantity).Should().Equal(322m, 315m);
    }

    [Fact]
    public async Task An_existing_job_without_a_target_never_plans_more_than_its_quantity()
    {
        var (db, job, _, _) = await SeedPlannedJobAsync(null, 300m);

        var built = await Service(db).BuildAsync(job.Id, default);

        built.TargetQuantity.Should().Be(300m);
        built.Operations.Select(o => o.PlannedQuantity).Should().Equal(300m, 300m);
    }

    [Fact]
    public async Task The_job_is_complete_when_the_last_step_good_quantity_reaches_the_target_not_the_start()
    {
        var (db, job, first, last) = await SeedPlannedJobAsync(300m, 322m);
        db.JobOperations.AddRange(
            new JobOperation { JobId = job.Id, OperationId = first.Id, StepNumber = 10, Title = "Mold", Status = JobOperationStatus.Complete, CompletedQuantity = 316m, ScrapQuantity = 6m },
            new JobOperation { JobId = job.Id, OperationId = last.Id, StepNumber = 20, Title = "Trim", Status = JobOperationStatus.InProgress, CompletedQuantity = 299m });
        await db.SaveChangesAsync();

        (await Service(db).BuildAsync(job.Id, default)).AllOperationsComplete.Should().BeFalse();

        var row = await db.JobOperations.SingleAsync(r => r.OperationId == last.Id);
        row.CompletedQuantity = 300m;
        await db.SaveChangesAsync();

        (await Service(db).BuildAsync(job.Id, default)).AllOperationsComplete.Should().BeTrue();
    }
}
