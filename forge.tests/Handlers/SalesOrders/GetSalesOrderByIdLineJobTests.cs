using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class GetSalesOrderByIdLineJobTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    [Fact]
    public async Task Line_jobs_report_IsComplete_for_completed_disposed_or_archived_work_orders()
    {
        var customer = new Customer { Id = 3, Name = "Detail Co" };
        _db.Customers.Add(customer);
        _db.JobStages.Add(new JobStage { Id = 11, TrackTypeId = 1, Name = "In Production", Code = "in_production" });
        var so = new SalesOrder
        {
            Id = 70, OrderNumber = "SO-00070", CustomerId = customer.Id, Customer = customer,
            Status = SalesOrderStatus.InProduction,
        };
        var line = new SalesOrderLine { Id = 71, Description = "Widget", Quantity = 4m, UnitPrice = 1m, LineNumber = 1 };
        so.Lines.Add(line);
        _db.SalesOrders.Add(so);

        Job Work(int id, Action<Job>? shape = null)
        {
            var job = new Job
            {
                Id = id, JobNumber = $"J-{id}", Title = "Build", TrackTypeId = 1, CurrentStageId = 11,
                SalesOrderLineId = line.Id,
            };
            shape?.Invoke(job);
            return job;
        }

        _db.Jobs.AddRange(
            Work(1),
            Work(2, j => j.CompletedDate = DateTimeOffset.UnixEpoch),
            Work(3, j => j.Disposition = JobDisposition.Scrap),
            Work(4, j => j.IsArchived = true));
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var handler = new GetSalesOrderByIdHandler(new SalesOrderRepository(_db), _db, userManager.Object);

        var result = await handler.Handle(new GetSalesOrderByIdQuery(so.Id), CancellationToken.None);

        var jobs = result.Lines.Single().Jobs.ToDictionary(j => j.Id, j => j.IsComplete);
        jobs.Should().BeEquivalentTo(new Dictionary<int, bool>
        {
            [1] = false,
            [2] = true,
            [3] = true,
            [4] = true,
        });
    }

    [Fact]
    public async Task Lines_report_NeedsJob_using_the_same_rule_as_job_creation()
    {
        var customer = new Customer { Id = 4, Name = "Mixed Co" };
        _db.Customers.Add(customer);
        Part AddPart(int id, ProcurementSource source, bool routed)
        {
            var part = new Part { Id = id, PartNumber = $"P-{id}", Name = $"P-{id}", ProcurementSource = source };
            _db.Parts.Add(part);
            if (routed)
                _db.Operations.Add(new Operation { PartId = id, StepNumber = 10, Title = "Mill" });
            return part;
        }

        var make = AddPart(201, ProcurementSource.Make, routed: false);
        var buy = AddPart(202, ProcurementSource.Buy, routed: false);
        var routedBuy = AddPart(203, ProcurementSource.Buy, routed: true);
        var so = new SalesOrder
        {
            Id = 80, OrderNumber = "SO-00080", CustomerId = customer.Id, Customer = customer,
            Status = SalesOrderStatus.Confirmed,
        };
        so.Lines.Add(new SalesOrderLine { Id = 81, PartId = make.Id, Part = make, Description = "Make", Quantity = 1m, UnitPrice = 1m, LineNumber = 1 });
        so.Lines.Add(new SalesOrderLine { Id = 82, PartId = buy.Id, Part = buy, Description = "Buy", Quantity = 1m, UnitPrice = 1m, LineNumber = 2 });
        so.Lines.Add(new SalesOrderLine { Id = 83, PartId = routedBuy.Id, Part = routedBuy, Description = "Routed buy", Quantity = 1m, UnitPrice = 1m, LineNumber = 3 });
        so.Lines.Add(new SalesOrderLine { Id = 84, Description = "Freight", Quantity = 1m, UnitPrice = 1m, LineNumber = 4 });
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var handler = new GetSalesOrderByIdHandler(new SalesOrderRepository(_db), _db, userManager.Object);

        var result = await handler.Handle(new GetSalesOrderByIdQuery(so.Id), CancellationToken.None);

        result.Lines.ToDictionary(l => l.Id, l => l.NeedsJob).Should().BeEquivalentTo(new Dictionary<int, bool>
        {
            [81] = true,
            [82] = false,
            [83] = true,
            [84] = false,
        });
    }

    [Fact]
    public async Task Lines_report_whether_they_ship_goods()
    {
        var customer = new Customer { Id = 5, Name = "Service Co" };
        _db.Customers.Add(customer);
        _db.Parts.AddRange(
            new Part { Id = 51, PartNumber = "P-51", Name = "Bracket" },
            new Part { Id = 52, PartNumber = "SVC-SETUP", Name = "Setup charge", InventoryClass = InventoryClass.Service });
        var so = new SalesOrder { Id = 90, OrderNumber = "SO-00090", CustomerId = customer.Id, Customer = customer };
        so.Lines.Add(new SalesOrderLine { Id = 91, PartId = 51, Description = "Bracket", Quantity = 2m, UnitPrice = 1m, LineNumber = 1 });
        so.Lines.Add(new SalesOrderLine { Id = 92, PartId = 52, Description = "Setup", Quantity = 1m, UnitPrice = 50m, LineNumber = 2 });
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var handler = new GetSalesOrderByIdHandler(new SalesOrderRepository(_db), _db, userManager.Object);

        var result = await handler.Handle(new GetSalesOrderByIdQuery(so.Id), CancellationToken.None);

        result.Lines.Single(l => l.Id == 91).ShipsGoods.Should().BeTrue();
        result.Lines.Single(l => l.Id == 92).ShipsGoods.Should().BeFalse();
    }
}
