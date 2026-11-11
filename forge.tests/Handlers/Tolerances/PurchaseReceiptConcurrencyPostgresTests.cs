using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Tolerances;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Tolerances;

[Collection(PostgresCollection.Name)]
public sealed class PurchaseReceiptConcurrencyPostgresTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<(int PurchaseOrderId, int LineId)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var vendor = new Vendor { CompanyName = Unique("Vendor") };
        var part = new Part { PartNumber = Unique("P"), Name = "Resin", Description = "Resin" };
        db.AddRange(vendor, part);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = Unique("PO"), VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        po.Lines.Add(new PurchaseOrderLine
        {
            PartId = part.Id, Description = "Resin", OrderedQuantity = 100m, UnitPrice = 2m, OverReceiptTolerancePct = 5m,
        });
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        return (po.Id, po.Lines.Single().Id);
    }

    private async Task<Exception?> ReceiveAsync(int purchaseOrderId, int lineId, decimal quantity)
    {
        await using var db = fixture.CreateContext();
        var handler = new ReceiveItemsHandler(
            new PurchaseOrderRepository(db), TestCaller.ClockAt(Now), new Mock<MediatR.IMediator>().Object, TestCaller.As(1), db);
        try
        {
            await handler.Handle(
                new ReceiveItemsCommand(purchaseOrderId, [new ReceiveLineModel(lineId, quantity, null, null)]),
                CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task Two_receipts_at_once_add_up_and_the_one_past_the_band_is_refused()
    {
        var (purchaseOrderId, lineId) = await SeedAsync();

        var outcomes = await Task.WhenAll(
            Task.Run(() => ReceiveAsync(purchaseOrderId, lineId, 60m)),
            Task.Run(() => ReceiveAsync(purchaseOrderId, lineId, 60m)));

        outcomes.Count(o => o is null).Should().Be(1);
        outcomes.Single(o => o is not null).Should().BeOfType<QuantityToleranceException>();
        await using var db = fixture.CreateContext();
        (await db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.Id == lineId)).ReceivedQuantity.Should().Be(60m);
        (await db.ReceivingRecords.AsNoTracking().Where(r => r.PurchaseOrderLineId == lineId).SumAsync(r => r.QuantityReceived))
            .Should().Be(60m);
    }

    [Fact]
    public async Task Two_receipts_at_once_inside_the_band_are_both_counted()
    {
        var (purchaseOrderId, lineId) = await SeedAsync();

        var outcomes = await Task.WhenAll(
            Task.Run(() => ReceiveAsync(purchaseOrderId, lineId, 40m)),
            Task.Run(() => ReceiveAsync(purchaseOrderId, lineId, 45m)));

        outcomes.Should().AllSatisfy(o => o.Should().BeNull());
        await using var db = fixture.CreateContext();
        (await db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.Id == lineId)).ReceivedQuantity.Should().Be(85m);
    }
}
