using FluentAssertions;
using Moq;
using Forge.Api.Features.AutoPo;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.AutoPo;

public class PurchaseOrderGeneratorTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly PurchaseOrderGenerator _generator;

    public PurchaseOrderGeneratorTests()
    {
        _generator = new PurchaseOrderGenerator(
            _db, new PurchaseOrderRepository(_db), Mock.Of<IBarcodeService>());
    }

    private static List<(int PartId, string Description, int Quantity, decimal UnitPrice, DateTimeOffset NeededBy)> OneLine()
        => [(1, "Widget", 5, 0m, DateTimeOffset.UtcNow.AddDays(7))];

    [Fact]
    public async Task GeneratePurchaseOrder_SetsAutoMrpOriginWithReference()
    {
        // S4b provenance — the generator only runs from the demand-driven
        // AutoPurchaseOrderJob, so every PO it emits is AutoMrp.
        _db.Vendors.Add(new Vendor { Id = 3, CompanyName = "Acme Supply" });
        await _db.SaveChangesAsync();

        var po = await _generator.GeneratePurchaseOrder(
            3, OneLine(), PurchaseOrderStatus.Draft, "notes", CancellationToken.None,
            originReference: "Demand analysis SO(s) 7, 9");

        po.OriginSource.Should().Be(PoOriginSource.AutoMrp);
        po.OriginUserId.Should().BeNull();
        po.OriginReference.Should().Be("Demand analysis SO(s) 7, 9");
    }

    [Fact]
    public async Task GeneratePurchaseOrder_TruncatesOverlongReferenceToColumnLength()
    {
        _db.Vendors.Add(new Vendor { Id = 3, CompanyName = "Acme Supply" });
        await _db.SaveChangesAsync();

        var overlong = new string('x', 250);
        var po = await _generator.GeneratePurchaseOrder(
            3, OneLine(), PurchaseOrderStatus.Draft, null, CancellationToken.None,
            originReference: overlong);

        po.OriginReference.Should().HaveLength(200);
    }
}
