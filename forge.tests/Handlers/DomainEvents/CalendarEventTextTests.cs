using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Terminology;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class CalendarEventTextTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PurchaseOrderDeliveryTitle_DoesNotRepeatThePoPrefix()
    {
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Delta Supply" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-1001", VendorId = vendor.Id, ExpectedDeliveryDate = Due };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        await new OnPurchaseOrderCreated_CreateDeliveryEvent(
                db, new TerminologyLabels(db), NullLogger<OnPurchaseOrderCreated_CreateDeliveryEvent>.Instance)
            .Handle(new PurchaseOrderCreatedEvent(po.Id, 1), CancellationToken.None);

        var evt = await db.Events.SingleAsync();
        evt.Title.Should().Be("PO-1001 Expected Delivery from Delta Supply");
        evt.Description.Should().Be("Expected delivery for Purchase Order PO-1001 from Delta Supply.");
    }

    [Fact]
    public async Task JobDueEvent_UsesTheInstallTermForJob()
    {
        using var db = TestDbContextFactory.Create();
        db.TerminologyEntries.Add(new TerminologyEntry { Key = "entity_job", Label = "Work Order" });
        var job = new Job { JobNumber = "J-4", Title = "Bracket run", DueDate = Due };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        await new OnJobCreated_CreateDueDateEvent(
                db, new TerminologyLabels(db), NullLogger<OnJobCreated_CreateDueDateEvent>.Instance)
            .Handle(new JobCreatedEvent(job.Id, 1), CancellationToken.None);

        var evt = await db.Events.SingleAsync();
        evt.Title.Should().Be("Work Order Due: J-4 - Bracket run");
        evt.Description.Should().Be("Due date for Work Order J-4: Bracket run.");
    }

    [Fact]
    public async Task SalesOrderMilestone_FormatsTheQuantityWithoutTrailingZeros()
    {
        using var db = TestDbContextFactory.Create();
        var so = new SalesOrder { OrderNumber = "SO-12", RequestedDeliveryDate = Due };
        so.Lines.Add(new SalesOrderLine { LineNumber = 1, Description = "Bracket", Quantity = 5000.0000m });
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        await new OnSalesOrderConfirmed_CreateDeliveryMilestones(
                db, new TerminologyLabels(db), NullLogger<OnSalesOrderConfirmed_CreateDeliveryMilestones>.Instance)
            .Handle(new SalesOrderConfirmedEvent(so.Id, 1), CancellationToken.None);

        var evt = await db.Events.SingleAsync();
        evt.Description.Should().Be("Delivery milestone for Sales Order SO-12, Line 1. Quantity: 5000.");
    }
}
