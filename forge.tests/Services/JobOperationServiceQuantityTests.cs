using FluentAssertions;
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
}
