using FluentAssertions;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// QA-critical regression — the list merges Draft entity-SO rows (id = SalesOrder.Id)
/// with Job-projected rows (id = Job.Id). Those sequences are unrelated, so the UI
/// must never send the row id to /orders/{id}; it opens by SalesOrderId instead.
/// These pin the identity fields on both row kinds and on the by-id projection.
/// </summary>
public class GetSalesOrdersListJobRowIdentityTests
{
    private readonly AppDbContext _db;
    private readonly GetSalesOrdersListHandler _handler;

    public GetSalesOrdersListJobRowIdentityTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetSalesOrdersListHandler(_db);
    }

    private async Task<(SalesOrder So, Job Job)> SeedConfirmedOrderWithJobAsync()
    {
        var customer = new Customer { Id = 1, Name = "Job Row Co" };
        _db.Customers.Add(customer);

        // Deliberately give the SO and the Job DIFFERENT ids so an id-space mixup
        // cannot pass by coincidence.
        var so = new SalesOrder
        {
            Id = 501,
            OrderNumber = "SO-00001",
            CustomerId = customer.Id,
            Customer = customer,
            Status = SalesOrderStatus.Confirmed,
        };
        _db.SalesOrders.Add(so);

        var line = new SalesOrderLine
        {
            Id = 601, SalesOrderId = so.Id, Description = "Widget",
            Quantity = 2m, UnitPrice = 5m, LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);

        var stage = new JobStage { Id = 71, TrackTypeId = 7, Name = "Order Confirmed", Code = "order_confirmed" };
        _db.JobStages.Add(stage);

        var job = new Job
        {
            Id = 42,
            JobNumber = "J-5",
            Title = "Build widgets",
            TrackTypeId = 7,
            CurrentStageId = stage.Id,
            CustomerId = customer.Id,
            SalesOrderLineId = line.Id,
            QuotedPrice = 10m,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return (so, job);
    }

    [Fact]
    public async Task Job_projected_row_carries_the_originating_SalesOrder_id()
    {
        var (so, job) = await SeedConfirmedOrderWithJobAsync();

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery()), CancellationToken.None);

        var row = result.Items.Single(i => i.OrderNumber == job.JobNumber);
        row.Id.Should().Be(job.Id, "the Job block keys rows by Job id");
        row.JobId.Should().Be(job.Id);
        row.SalesOrderId.Should().Be(so.Id,
            "clicking the row must open the SO the job was created from, not whatever SO shares the Job's integer id");
    }

    [Fact]
    public async Task Job_without_a_sales_order_link_has_null_SalesOrderId()
    {
        var (_, job) = await SeedConfirmedOrderWithJobAsync();
        job.SalesOrderLineId = null;
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery()), CancellationToken.None);

        var row = result.Items.Single(i => i.OrderNumber == job.JobNumber);
        row.SalesOrderId.Should().BeNull("a board-created job with no SO must not pretend to have one");
        row.JobId.Should().Be(job.Id, "the UI falls back to opening the job detail");
    }

    [Fact]
    public async Task Draft_row_carries_its_own_id_as_SalesOrderId_and_no_JobId()
    {
        var customer = new Customer { Id = 2, Name = "Draft Co" };
        _db.Customers.Add(customer);
        _db.SalesOrders.Add(new SalesOrder
        {
            Id = 777, OrderNumber = "SO-DRAFT-9", CustomerId = customer.Id,
            Customer = customer, Status = SalesOrderStatus.Draft,
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(
            new GetSalesOrdersListQuery(new SalesOrderListQuery()), CancellationToken.None);

        var row = result.Items.Single(i => i.OrderNumber == "SO-DRAFT-9");
        row.SalesOrderId.Should().Be(777);
        row.JobId.Should().BeNull();
    }

    [Fact]
    public async Task ById_projection_resolves_the_same_identity_fields()
    {
        var (so, job) = await SeedConfirmedOrderWithJobAsync();
        var byIdHandler = new GetSalesOrderProjectionByIdHandler(_db);

        var row = await byIdHandler.Handle(
            new GetSalesOrderProjectionByIdQuery(job.Id), CancellationToken.None);

        row.Should().NotBeNull();
        row!.SalesOrderId.Should().Be(so.Id);
        row.JobId.Should().Be(job.Id);
    }
}
