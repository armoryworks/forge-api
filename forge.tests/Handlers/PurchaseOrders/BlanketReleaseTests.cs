using FluentAssertions;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class BlanketReleaseTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<PurchaseOrder> SeedBlanketAsync(decimal total, decimal released, decimal received)
    {
        var vendor = new Vendor { CompanyName = "Blanket Vendor" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder
        {
            PONumber = "PO-B1", VendorId = vendor.Id, IsBlanket = true,
            BlanketTotalQuantity = total, BlanketReleasedQuantity = released,
            Lines = { new PurchaseOrderLine { Description = "Resin", OrderedQuantity = total, ReceivedQuantity = received, UnitPrice = 1m } },
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return po;
    }

    [Fact]
    public async Task Remaining_counts_quantity_received_against_the_blanket_without_a_release()
    {
        var po = await SeedBlanketAsync(1650m, 0m, 881m);

        GetPurchaseOrderByIdHandler.BlanketRemaining(po).Should().Be(769m);
    }

    [Fact]
    public async Task Remaining_uses_the_released_quantity_when_it_exceeds_receipts()
    {
        var po = await SeedBlanketAsync(1650m, 1000m, 400m);

        GetPurchaseOrderByIdHandler.BlanketRemaining(po).Should().Be(650m);
    }

    [Fact]
    public async Task A_release_above_the_blanket_remaining_is_rejected()
    {
        var po = await SeedBlanketAsync(500m, 400m, 0m);

        var act = () => new CreatePurchaseOrderReleaseHandler(_db).Handle(
            new CreatePurchaseOrderReleaseCommand(po.Id, new CreatePurchaseOrderReleaseRequestModel { PurchaseOrderLineId = po.Lines.Single().Id, Quantity = 101m }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        po.BlanketReleasedQuantity.Should().Be(400m);
    }

    [Fact]
    public async Task A_release_within_the_blanket_remaining_is_recorded()
    {
        var po = await SeedBlanketAsync(500m, 400m, 0m);

        await new CreatePurchaseOrderReleaseHandler(_db).Handle(
            new CreatePurchaseOrderReleaseCommand(po.Id, new CreatePurchaseOrderReleaseRequestModel { PurchaseOrderLineId = po.Lines.Single().Id, Quantity = 100m }),
            CancellationToken.None);

        po.BlanketReleasedQuantity.Should().Be(500m);
    }

    [Fact]
    public async Task Raising_a_release_above_the_blanket_total_is_rejected()
    {
        var po = await SeedBlanketAsync(500m, 0m, 0m);
        var create = new CreatePurchaseOrderReleaseHandler(_db);
        await create.Handle(
            new CreatePurchaseOrderReleaseCommand(po.Id, new CreatePurchaseOrderReleaseRequestModel { PurchaseOrderLineId = po.Lines.Single().Id, Quantity = 300m }),
            CancellationToken.None);

        var act = () => new UpdatePurchaseOrderReleaseHandler(_db).Handle(
            new UpdatePurchaseOrderReleaseCommand(po.Id, 1, new UpdatePurchaseOrderReleaseRequestModel { Quantity = 501m }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        po.BlanketReleasedQuantity.Should().Be(300m);
    }
}
