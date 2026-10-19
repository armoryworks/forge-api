using FluentAssertions;

using Forge.Api.Features.Purchasing;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Purchasing;

public class GetRfqByIdPoNumberTests
{
    [Fact]
    public async Task Detail_names_the_generated_purchase_order_by_its_number()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PRT-1", Name = "Bracket" };
        var vendor = new Vendor { CompanyName = "Delta Supply" };
        db.Parts.Add(part);
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-00017", VendorId = vendor.Id };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        var rfq = new RequestForQuote { RfqNumber = "RFQ-1", PartId = part.Id, Quantity = 5, GeneratedPurchaseOrderId = po.Id };
        db.RequestForQuotes.Add(rfq);
        await db.SaveChangesAsync();

        var result = await new GetRfqByIdHandler(db).Handle(new GetRfqByIdQuery(rfq.Id), CancellationToken.None);

        result.GeneratedPurchaseOrderId.Should().Be(po.Id);
        result.GeneratedPurchaseOrderNumber.Should().Be("PO-00017");
    }
}
