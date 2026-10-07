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
        var rack = new StorageLocation { Name = "Rack A", LocationType = LocationType.Rack };
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
        byDescription["active"].PartDefaultBinPath.Should().Be("Rack A / A1");
        byDescription["inactive"].PartDefaultBinPath.Should().BeNull();
        byDescription["inactive"].PartDefaultBinId.Should().BeNull();
        byDescription["shelf"].PartDefaultBinId.Should().BeNull();
        byDescription["none"].PartDefaultBinId.Should().BeNull();
        byDescription["service"].PartNumber.Should().BeNull();
        byDescription["service"].PartDefaultBinId.Should().BeNull();
    }
}
