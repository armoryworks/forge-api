using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class RfqAwardPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Awarding_an_rfq_for_a_part_with_a_long_description_creates_the_po_with_the_description_clipped_and_the_quantity_kept()
    {
        await using var db = fixture.CreateContext();
        var longDescription = new string('x', 1500);
        var part = new Part
        {
            PartNumber = $"RFQ-{Guid.NewGuid():N}"[..16],
            Name = "Long description part",
            Description = longDescription,
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
        };
        var vendor = new Vendor { CompanyName = $"RFQ vendor {Guid.NewGuid():N}"[..24] };
        db.AddRange(part, vendor);
        await db.SaveChangesAsync();

        var rfq = new RequestForQuote
        {
            RfqNumber = $"R{Guid.NewGuid():N}"[..18],
            PartId = part.Id,
            Quantity = 12.5m,
            RequiredDate = DateTimeOffset.UtcNow.AddDays(30),
            Status = RfqStatus.Sent,
            VendorResponses =
            {
                new RfqVendorResponse { VendorId = vendor.Id, ResponseStatus = RfqResponseStatus.Received, UnitPrice = 2.5m },
            },
        };
        db.RequestForQuotes.Add(rfq);
        await db.SaveChangesAsync();

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var service = new RfqService(db, clock.Object, new PurchaseOrderRepository(db));

        var poId = await service.AwardAndCreatePoAsync(rfq.Id, rfq.VendorResponses.Single().Id, CancellationToken.None);

        var line = await db.PurchaseOrderLines.AsNoTracking().SingleAsync(l => l.PurchaseOrderId == poId);
        line.Description.Should().Be(longDescription[..PurchaseOrderLine.DescriptionMaxLength]);
        line.OrderedQuantity.Should().Be(12.5m);
    }
}
