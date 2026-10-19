using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class GetPurchaseOrderByIdHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly GetPurchaseOrderByIdHandler _handler;

    public GetPurchaseOrderByIdHandlerTests()
    {
        _handler = new GetPurchaseOrderByIdHandler(_repo.Object, TestDbContextFactory.Create());
    }

    [Fact]
    public async Task Handle_PartlessLine_ReturnsLineWithNullPartNumber()
    {
        var po = new PurchaseOrder
        {
            Id = 7,
            PONumber = "PO-0007",
            VendorId = 3,
            Vendor = new Vendor { Id = 3, CompanyName = "Vendor" },
        };
        po.Lines.Add(new PurchaseOrderLine
        {
            Id = 70,
            PartId = 8,
            Part = new Part { Id = 8, PartNumber = "P-008", Name = "Bracket" },
            Description = "Bracket",
            OrderedQuantity = 2,
            UnitPrice = 10m,
        });
        po.Lines.Add(new PurchaseOrderLine
        {
            Id = 71,
            PartId = null,
            Description = "Heat treat service",
            OrderedQuantity = 1,
            UnitPrice = 250m,
            Notes = "Certs required",
        });
        _repo.Setup(r => r.FindWithDetailsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var result = await _handler.Handle(new GetPurchaseOrderByIdQuery(7), CancellationToken.None);

        result.Lines.Should().HaveCount(2);
        result.Lines.Single(l => l.Id == 70).PartNumber.Should().Be("P-008");
        var service = result.Lines.Single(l => l.Id == 71);
        service.PartId.Should().BeNull();
        service.PartNumber.Should().BeNull();
        service.Description.Should().Be("Heat treat service");
        service.LineTotal.Should().Be(250m);
    }

    [Fact]
    public async Task Handle_ReturnsTheOptimisticLockVersionAsRowVersion()
    {
        var po = new PurchaseOrder
        {
            Id = 9,
            PONumber = "PO-0009",
            VendorId = 3,
            Vendor = new Vendor { Id = 3, CompanyName = "Vendor" },
            Version = 4,
        };
        _repo.Setup(r => r.FindWithDetailsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var result = await _handler.Handle(new GetPurchaseOrderByIdQuery(9), CancellationToken.None);

        result.RowVersion.Should().Be(4u, "the UI caches it as the If-Match for the next header edit");
    }

    private static async Task<Part> AddPartAsync(AppDbContext db, string partNumber, StorageLocation? defaultBin)
    {
        if (defaultBin is not null)
        {
            db.Set<StorageLocation>().Add(defaultBin);
            await db.SaveChangesAsync();
        }

        var part = new Part { PartNumber = partNumber, Description = partNumber, DefaultBinId = defaultBin?.Id };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task Lines_CarryOnlyActiveBinDefaults_AndPartlessLinesLoad()
    {
        var db = TestDbContextFactory.Create();
        var warehouse = new StorageLocation { Name = "Warehouse", LocationType = LocationType.Area };
        db.Set<StorageLocation>().Add(warehouse);
        await db.SaveChangesAsync();
        var rack = new StorageLocation { Name = "Rack A", LocationType = LocationType.Rack, ParentId = warehouse.Id };
        db.Set<StorageLocation>().Add(rack);
        await db.SaveChangesAsync();
        var active = await AddPartAsync(db, "P-ACTIVE", new StorageLocation { Name = "A1", LocationType = LocationType.Bin, ParentId = rack.Id });
        var inactive = await AddPartAsync(db, "P-INACTIVE", new StorageLocation { Name = "A2", LocationType = LocationType.Bin, IsActive = false });
        var shelf = await AddPartAsync(db, "P-SHELF", new StorageLocation { Name = "Shelf", LocationType = LocationType.Shelf });
        var noBin = await AddPartAsync(db, "P-NONE", null);

        var vendor = new Vendor { CompanyName = "Vendor" };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        foreach (var (partId, description) in new (int?, string)[]
                 {
                     (active.Id, "active"), (inactive.Id, "inactive"), (shelf.Id, "shelf"), (noBin.Id, "none"), (null, "service"),
                 })
        {
            db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
            {
                PurchaseOrderId = po.Id, PartId = partId, Description = description, OrderedQuantity = 1m, UnitPrice = 1m,
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetPurchaseOrderByIdHandler(new PurchaseOrderRepository(db), db)
            .Handle(new GetPurchaseOrderByIdQuery(po.Id), CancellationToken.None);

        var byDescription = result.Lines.ToDictionary(l => l.Description);
        byDescription["active"].PartDefaultBinId.Should().Be(active.DefaultBinId);
        byDescription["active"].PartDefaultBinPath.Should().Be("Warehouse / Rack A / A1");
        byDescription["inactive"].PartDefaultBinPath.Should().BeNull();
        byDescription["inactive"].PartDefaultBinId.Should().BeNull();
        byDescription["shelf"].PartDefaultBinId.Should().BeNull();
        byDescription["none"].PartDefaultBinId.Should().BeNull();
        byDescription["service"].PartNumber.Should().BeNull();
        byDescription["service"].PartDefaultBinId.Should().BeNull();
    }

    [Fact]
    public async Task Detail_NamesTheBinAReceiptFallsBackTo_DefaultBinFirst()
    {
        var db = TestDbContextFactory.Create();
        db.Set<StorageLocation>().AddRange(
            new StorageLocation { Name = "A1", LocationType = LocationType.Bin, SortOrder = 1 },
            new StorageLocation { Name = "Dock", LocationType = LocationType.Bin, SortOrder = 2, IsDefault = true },
            new StorageLocation { Name = "Old", LocationType = LocationType.Bin, SortOrder = 0, IsDefault = true, IsActive = false });
        await db.SaveChangesAsync();
        var part = await AddPartAsync(db, "P-FALLBACK", null);
        var vendor = new Vendor { CompanyName = "Vendor" };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-FB", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id, Description = "stocked", OrderedQuantity = 1m, UnitPrice = 1m,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetPurchaseOrderByIdHandler(new PurchaseOrderRepository(db), db)
            .Handle(new GetPurchaseOrderByIdQuery(po.Id), CancellationToken.None);

        result.ReceivingFallbackBinPath.Should().Be("Dock");
        result.ReceivingFallbackBinId.Should().NotBeNull();
    }

    [Fact]
    public async Task Detail_WithNoActiveBin_HasNoFallbackBin()
    {
        var db = TestDbContextFactory.Create();
        var part = await AddPartAsync(db, "P-NOBINS", null);
        var vendor = new Vendor { CompanyName = "Vendor" };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-NB", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id, Description = "stocked", OrderedQuantity = 1m, UnitPrice = 1m,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetPurchaseOrderByIdHandler(new PurchaseOrderRepository(db), db)
            .Handle(new GetPurchaseOrderByIdQuery(po.Id), CancellationToken.None);

        result.ReceivingFallbackBinId.Should().BeNull();
        result.ReceivingFallbackBinPath.Should().BeNull();
    }

    [Fact]
    public async Task Lines_CarryTheirUnitAndItsPrecision()
    {
        var db = TestDbContextFactory.Create();
        var each = new UnitOfMeasure { Code = "ea", Name = "Each", Symbol = "ea", Category = UomCategory.Count, DecimalPlaces = 0 };
        var kilo = new UnitOfMeasure { Code = "kg", Name = "Kilogram", Category = UomCategory.Weight, DecimalPlaces = 3 };
        db.Set<UnitOfMeasure>().AddRange(each, kilo);
        await db.SaveChangesAsync();
        var bracket = new Part { PartNumber = "P-EA", Description = "bracket", StockUomId = each.Id };
        var resin = new Part { PartNumber = "P-KG", Description = "resin", StockUomId = each.Id };
        db.Set<Part>().AddRange(bracket, resin);
        var vendor = new Vendor { CompanyName = "Vendor" };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-UOM", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();
        db.Set<PurchaseOrderLine>().AddRange(
            new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = bracket.Id, Description = "bracket", OrderedQuantity = 4m, UnitPrice = 1m },
            new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = resin.Id, Description = "resin", OrderedQuantity = 2.5m, UnitPrice = 1m, UomId = kilo.Id },
            new PurchaseOrderLine { PurchaseOrderId = po.Id, Description = "service", OrderedQuantity = 1m, UnitPrice = 1m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new GetPurchaseOrderByIdHandler(new PurchaseOrderRepository(db), db)
            .Handle(new GetPurchaseOrderByIdQuery(po.Id), CancellationToken.None);

        var byDescription = result.Lines.ToDictionary(l => l.Description);
        byDescription["bracket"].UnitSymbol.Should().Be("ea");
        byDescription["bracket"].QuantityDecimalPlaces.Should().Be(0);
        byDescription["resin"].UnitSymbol.Should().Be("kg");
        byDescription["resin"].QuantityDecimalPlaces.Should().Be(3);
        byDescription["service"].UnitSymbol.Should().BeNull();
        byDescription["service"].QuantityDecimalPlaces.Should().BeNull();
    }
}
