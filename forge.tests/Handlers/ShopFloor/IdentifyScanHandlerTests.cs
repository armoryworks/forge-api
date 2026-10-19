using Bogus;
using FluentAssertions;

using Microsoft.AspNetCore.Identity;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class IdentifyScanHandlerTests
{
    private readonly AppDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly IdentifyScanHandler _handler;
    private readonly Faker _faker = new();

    public IdentifyScanHandlerTests()
    {
        _db = TestDbContextFactory.Create();

        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new IdentifyScanHandler(_db, _userManager.Object);
    }

    [Fact]
    public async Task Handle_KnownBarcodeValue_ReturnsMatch()
    {
        // Arrange
        var trackType = new TrackType { Name = "Production", Code = "PROD", IsActive = true };
        _db.TrackTypes.Add(trackType);
        await _db.SaveChangesAsync();

        var stage = new JobStage
        {
            TrackTypeId = trackType.Id,
            Name = "In Production",
            Code = "in_production",
            SortOrder = 1,
            Color = "#22c55e",
            IsActive = true,
        };
        _db.JobStages.Add(stage);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-1001",
            Title = "Test Job",
            TrackTypeId = trackType.Id,
            CurrentStageId = stage.Id,
            Priority = JobPriority.Normal,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var barcode = new Barcode
        {
            Value = "BC-JOB-1001",
            EntityType = BarcodeEntityType.Job,
            JobId = job.Id,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("BC-JOB-1001");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("job");
        result.EntityId.Should().Be(job.Id);
        result.EntityNumber.Should().Be("JOB-1001");
        result.EntityTitle.Should().Be("Test Job");
        result.StageName.Should().Be("In Production");
        result.StageColor.Should().Be("#22c55e");
    }

    [Fact]
    public async Task Handle_UnknownValue_ReturnsNotFound()
    {
        // Arrange
        // UserManager.Users must return a queryable that supports async (EF InMemory does)
        _userManager.Setup(m => m.Users).Returns(_db.Users);

        var query = new IdentifyScanQuery("UNKNOWN-BARCODE-VALUE");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("unknown");
        result.EntityId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UserScanIdentifier_ReturnsEmployee()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Daniel",
            LastName = "Hartman",
            UserName = "daniel@example.com",
            Email = "daniel@example.com",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.UserScanIdentifiers.Add(new UserScanIdentifier
        {
            UserId = user.Id,
            IdentifierType = "rfid",
            IdentifierValue = "RFID-12345",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        _userManager.Setup(m => m.FindByIdAsync(user.Id.ToString()))
            .ReturnsAsync(user);

        var query = new IdentifyScanQuery("RFID-12345");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("employee");
        result.EntityId.Should().Be(user.Id);
        result.EntityTitle.Should().Be("Hartman, Daniel");
    }

    [Fact]
    public async Task Handle_LegacyEmployeeBarcode_ReturnsEmployee()
    {
        // Arrange
        var user = new ApplicationUser
        {
            FirstName = "Alice",
            LastName = "Johnson",
            UserName = "alice@example.com",
            Email = "alice@example.com",
            EmployeeBarcode = "EMP-99",
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Mock Users queryable via UserManager
        _userManager.Setup(m => m.Users)
            .Returns(_db.Users);

        var query = new IdentifyScanQuery("EMP-99");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("employee");
        result.EntityId.Should().Be(user.Id);
        result.EntityTitle.Should().Be("Johnson, Alice");
    }

    [Fact]
    public async Task Handle_PartBarcode_ReturnsPart()
    {
        // Arrange
        var barcode = new Barcode
        {
            Value = "BC-PART-42",
            EntityType = BarcodeEntityType.Part,
            PartId = 42,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("BC-PART-42");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("part");
        result.EntityId.Should().Be(42);
    }

    [Fact]
    public async Task Handle_WhitespaceAroundValue_TrimsAndMatches()
    {
        // Arrange
        var barcode = new Barcode
        {
            Value = "BC-ASSET-7",
            EntityType = BarcodeEntityType.Asset,
            AssetId = 7,
            IsActive = true,
        };
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();

        var query = new IdentifyScanQuery("  BC-ASSET-7  ");

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.ScanType.Should().Be("asset");
        result.EntityId.Should().Be(7);
    }

    private async Task<ScanIdentificationResult> ScanAsync(Barcode barcode)
    {
        barcode.IsActive = true;
        _db.Barcodes.Add(barcode);
        await _db.SaveChangesAsync();
        return await _handler.Handle(new IdentifyScanQuery(barcode.Value), CancellationToken.None);
    }

    [Fact]
    public async Task Handle_SalesOrderBarcode_ReturnsTheOrderNumberOnly()
    {
        var order = new SalesOrder { OrderNumber = "SO-0042", CustomerId = 1 };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-SO-42", EntityType = BarcodeEntityType.SalesOrder, SalesOrderId = order.Id });

        result.Should().Be(new ScanIdentificationResult("sales-order", order.Id, "SO-0042"));
    }

    [Fact]
    public async Task Handle_PurchaseOrderBarcode_ReturnsThePoNumberOnly()
    {
        var po = new PurchaseOrder { PONumber = "PO-0007", VendorId = 1 };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-PO-7", EntityType = BarcodeEntityType.PurchaseOrder, PurchaseOrderId = po.Id });

        result.Should().Be(new ScanIdentificationResult("purchase-order", po.Id, "PO-0007"));
    }

    [Fact]
    public async Task Handle_PartBarcode_ReturnsThePartNumber()
    {
        var part = new Part { PartNumber = "BRK-100", Name = "Bracket" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-BRK-100", EntityType = BarcodeEntityType.Part, PartId = part.Id });

        result.Should().Be(new ScanIdentificationResult("part", part.Id, "BRK-100"));
    }

    [Fact]
    public async Task Handle_LotBarcode_ReturnsTheLotNumber()
    {
        var lot = new LotRecord { LotNumber = "LOT-2026-001", PartId = 1 };
        _db.LotRecords.Add(lot);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-LOT-1", EntityType = BarcodeEntityType.Lot, LotRecordId = lot.Id });

        result.Should().Be(new ScanIdentificationResult("lot", lot.Id, "LOT-2026-001"));
    }

    [Fact]
    public async Task Handle_LocationBarcode_ReturnsTheLocationPath()
    {
        var area = new StorageLocation { Name = "Warehouse", LocationType = LocationType.Area };
        _db.StorageLocations.Add(area);
        await _db.SaveChangesAsync();
        var bin = new StorageLocation { Name = "Bin A1", LocationType = LocationType.Bin, ParentId = area.Id };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-BIN-A1", EntityType = BarcodeEntityType.StorageLocation, StorageLocationId = bin.Id });

        result.Should().Be(new ScanIdentificationResult("storage-location", bin.Id, null, "Warehouse / Bin A1"));
    }

    [Fact]
    public async Task Handle_CustomerOwnedAssetBarcode_ReturnsNoName()
    {
        var asset = new Asset { Name = "Customer mold", IsCustomerOwned = true };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-MOLD-1", EntityType = BarcodeEntityType.Asset, AssetId = asset.Id });

        result.Should().Be(new ScanIdentificationResult("asset", asset.Id));
    }

    [Fact]
    public async Task Handle_ShopAssetBarcode_ReturnsTheAssetName()
    {
        var asset = new Asset { Name = "Press 3" };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync();

        var result = await ScanAsync(new Barcode { Value = "BC-PRESS-3", EntityType = BarcodeEntityType.Asset, AssetId = asset.Id });

        result.Should().Be(new ScanIdentificationResult("asset", asset.Id, "Press 3"));
    }
}
