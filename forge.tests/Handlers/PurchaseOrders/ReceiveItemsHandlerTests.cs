using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Handlers.PurchaseOrders;

/// <summary>
/// Bought-parts effort PR3 — ReceiveItems handler tests cover the four
/// allocation methods plus the freight-defaulting behavior.
/// </summary>
public class ReceiveItemsHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly Mock<IPurchaseOrderRepository> _capturedRepo;
    private readonly Mock<MediatR.IMediator> _mediator = new();
    private readonly Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor> _httpContext = new();
    private readonly IClock _clock = new SystemClock();
    private readonly ReceiveItemsHandler _handler;
    private readonly Mock<IInventoryRepository> _inventory = new();
    private readonly ReceiveItemsHandler _stockingHandler;
    private readonly List<BinContent> _addedContents = new();
    private readonly List<BinMovement> _addedMovements = new();

    private readonly List<ReceivingRecord> _addedRecords = new();

    public ReceiveItemsHandlerTests()
    {
        _capturedRepo = _repo;
        _capturedRepo.Setup(r => r.AddReceivingRecordAsync(It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ReceivingRecord, CancellationToken>((r, _) => _addedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") }));
        _httpContext.Setup(x => x.HttpContext).Returns(ctx);

        _handler = new ReceiveItemsHandler(_repo.Object, _clock, _mediator.Object, _httpContext.Object);

        _inventory.Setup(i => i.AddBinContentAsync(It.IsAny<BinContent>(), It.IsAny<CancellationToken>()))
            .Callback<BinContent, CancellationToken>((c, _) => _addedContents.Add(c))
            .Returns(Task.CompletedTask);
        _inventory.Setup(i => i.AddMovementAsync(It.IsAny<BinMovement>(), It.IsAny<CancellationToken>()))
            .Callback<BinMovement, CancellationToken>((m, _) => _addedMovements.Add(m))
            .Returns(Task.CompletedTask);
        _stockingHandler = new ReceiveItemsHandler(
            _repo.Object, _clock, _mediator.Object, _httpContext.Object, inventory: _inventory.Object);
    }

    private void GivenLocation(int id, bool isActive = true, LocationType type = LocationType.Bin, bool isDefault = false)
        => _inventory.Setup(i => i.FindLocationAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = id, Name = $"Loc {id}", IsActive = isActive, LocationType = type, IsDefault = isDefault });

    private PurchaseOrder GivenStockPo(decimal orderedQty, Part part, PartPurchaseUnit? purchaseUnit = null)
    {
        var po = PoWith(estimatedFreight: null, (1, part.Id, qty: orderedQty, unitPrice: 10m));
        po.Lines.First().Part = part;
        po.Lines.First().PurchaseUnit = purchaseUnit;
        po.Lines.First().PurchaseUnitId = purchaseUnit?.Id;
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        return po;
    }

    private static PurchaseOrder PoWith(decimal? estimatedFreight, params (int lineId, int partId, decimal qty, decimal unitPrice)[] lines)
    {
        var po = new PurchaseOrder
        {
            Id = 100,
            PONumber = "PO-100",
            VendorId = 1,
            EstimatedFreight = estimatedFreight,
            Status = PurchaseOrderStatus.Acknowledged,
        };
        foreach (var (lineId, partId, qty, price) in lines)
        {
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = lineId,
                PartId = partId,
                OrderedQuantity = qty,
                ReceivedQuantity = 0,
                UnitPrice = price,
                Description = $"Part {partId}",
            });
        }
        return po;
    }

    [Fact]
    public async Task Handle_ByExtendedValue_AllocatesProportionally()
    {
        // 100 freight, two lines: $200 and $600 extended → 25/75 split.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 10m, unitPrice: 20m),  // $200 extended
            (2, 11, qty: 6m, unitPrice: 100m)); // $600 extended
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 10m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 6m, StorageLocationId: null, Notes: null),
            },
            ActualFreight: 100m,
            FreightAllocationMethod: FreightAllocationMethod.ByExtendedValue), CancellationToken.None);

        _addedRecords.Should().HaveCount(2);
        _addedRecords[0].AllocatedFreight.Should().Be(25m);
        _addedRecords[1].AllocatedFreight.Should().Be(75m);
        _addedRecords.Should().AllSatisfy(r => r.ReceiptNumber.Should().NotBeNullOrEmpty());
        _addedRecords.Select(r => r.ReceiptNumber).Distinct().Should().HaveCount(1, "all records in one call share a receipt number");
    }

    [Fact]
    public async Task Handle_ByQuantity_AllocatesPerUnitEvenly()
    {
        // 90 freight, two lines: 10 + 5 units = 15 total → 60/30 split.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 10m, unitPrice: 5m),
            (2, 11, qty: 5m, unitPrice: 200m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 10m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 5m, StorageLocationId: null, Notes: null),
            },
            ActualFreight: 90m,
            FreightAllocationMethod: FreightAllocationMethod.ByQuantity), CancellationToken.None);

        _addedRecords[0].AllocatedFreight.Should().Be(60m);
        _addedRecords[1].AllocatedFreight.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_Manual_UsesCallerSuppliedPerLineFreight()
    {
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 5m, unitPrice: 10m),
            (2, 11, qty: 5m, unitPrice: 10m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null, ManualFreight: 70m),
                new(LineId: 2, Quantity: 5m, StorageLocationId: null, Notes: null, ManualFreight: 30m),
            },
            ActualFreight: 100m,
            FreightAllocationMethod: FreightAllocationMethod.Manual), CancellationToken.None);

        _addedRecords[0].AllocatedFreight.Should().Be(70m);
        _addedRecords[1].AllocatedFreight.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_NullActualFreight_DefaultsFromPoEstimate()
    {
        // PO has $50 estimated freight; caller doesn't pass an actual.
        // Records should record ActualFreight = $50 and allocate it.
        var po = PoWith(estimatedFreight: 50m,
            (1, 10, qty: 1m, unitPrice: 200m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 1m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedRecords.Single().ActualFreight.Should().Be(50m);
        _addedRecords.Single().AllocatedFreight.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_NullFreightEverywhere_LeavesAllocationNull()
    {
        // No PO estimate, no caller actual → record captures the line but
        // skips allocation. Pre-PR3 records also fall here.
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 2m, unitPrice: 5m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedRecords.Single().ActualFreight.Should().BeNull();
        _addedRecords.Single().AllocatedFreight.Should().BeNull();
    }

    // ── F-033: source-state whitelist guard ───────────────────────────────────

    [Theory]
    [InlineData(PurchaseOrderStatus.Submitted)]        // PO sent to vendor — receivable
    [InlineData(PurchaseOrderStatus.PartiallyReceived)] // partially in — still receivable
    public async Task Handle_WhitelistStatus_Processes(PurchaseOrderStatus status)
    {
        var po = PoWith(estimatedFreight: null, (1, 10, qty: 5m, unitPrice: 10m));
        po.Status = status;
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        // Act — must not throw; the guard passes and the handler runs to completion
        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _capturedRepo.Verify(r => r.AddReceivingRecordAsync(
            It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]      // F-033: NEW block — PO not yet sent to vendor
    [InlineData(PurchaseOrderStatus.Received)]   // all lines fully received — over-receive via status guard
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Handle_NonReceivableStatus_ThrowsInvalidOperation(PurchaseOrderStatus status)
    {
        var po = PoWith(estimatedFreight: null, (1, 10, qty: 5m, unitPrice: 10m));
        po.Status = status;
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        var act = () => _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Allowed: Submitted, Acknowledged, PartiallyReceived*");

        _capturedRepo.Verify(r => r.AddReceivingRecordAsync(
            It.IsAny<ReceivingRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PurchaseUnitLine_StocksBaseUnitsAndKeepsOrderUnitsOnLine()
    {
        var part = new Part { Id = 10, PartNumber = "BAR-1", DefaultBinId = 7 };
        var po = GivenStockPo(5m, part, new PartPurchaseUnit { Id = 3, PartId = 10, Label = "12 ft bar", ContentQuantity = 12m });
        GivenLocation(7);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null, LotNumber: "HT-4471") }),
            CancellationToken.None);

        po.Lines.First().ReceivedQuantity.Should().Be(2m);
        _addedRecords.Single().QuantityReceived.Should().Be(2m);
        _addedRecords.Single().LotNumber.Should().Be("HT-4471");
        var content = _addedContents.Single();
        content.Quantity.Should().Be(24m);
        content.LocationId.Should().Be(7);
        content.LotNumber.Should().Be("HT-4471");
        _addedMovements.Single().Quantity.Should().Be(24m);
        _addedMovements.Single().LotNumber.Should().Be("HT-4471");
    }

    [Fact]
    public async Task Handle_NoRequestedBin_UsesActivePartDefaultBin()
    {
        var part = new Part { Id = 10, PartNumber = "P-10", DefaultBinId = 7 };
        var po = GivenStockPo(5m, part);
        GivenLocation(7);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedContents.Single().LocationId.Should().Be(7);
        _addedMovements.Single().ToLocationId.Should().Be(7);
        _addedRecords.Single().StorageLocationId.Should().Be(7);
        _inventory.Verify(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RequestedBin_WinsOverPartDefault()
    {
        var part = new Part { Id = 10, PartNumber = "P-10", DefaultBinId = 7 };
        var po = GivenStockPo(5m, part);
        GivenLocation(7);
        GivenLocation(9);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: 9, Notes: null) }),
            CancellationToken.None);

        _addedContents.Single().LocationId.Should().Be(9);
    }

    [Theory]
    [InlineData(false, LocationType.Bin)]
    [InlineData(true, LocationType.Shelf)]
    public async Task Handle_RequestedLocationInactiveOrNotABin_RejectsBeforeRecording(bool isActive, LocationType type)
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });
        GivenLocation(9, isActive, type);

        var act = () => _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: 9, Notes: null) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _addedRecords.Should().BeEmpty();
        _addedContents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RequestedLocationMissing_ThrowsNotFound()
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });

        var act = () => _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: 404, Notes: null) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _addedRecords.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, LocationType.Bin)]
    [InlineData(true, LocationType.Shelf)]
    public async Task Handle_PartDefaultInactiveOrNotABin_FallsBackToActiveDefaultLocation(bool isActive, LocationType type)
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10", DefaultBinId = 7 });
        GivenLocation(7, isActive, type);
        _inventory.Setup(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<StorageLocation>
        {
            new() { Id = 20, Name = "A", LocationType = LocationType.Bin, IsActive = true },
            new() { Id = 21, Name = "Old main", LocationType = LocationType.Bin, IsActive = false, IsDefault = true },
            new() { Id = 22, Name = "Main", LocationType = LocationType.Bin, IsActive = true, IsDefault = true },
        });

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedContents.Single().LocationId.Should().Be(22);
    }

    [Fact]
    public async Task Handle_NoDefaults_UsesFirstActiveBin()
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });
        _inventory.Setup(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<StorageLocation>
        {
            new() { Id = 30, Name = "Area", LocationType = LocationType.Area, IsActive = true },
            new() { Id = 31, Name = "Retired", LocationType = LocationType.Bin, IsActive = false },
            new() { Id = 32, Name = "B-1", LocationType = LocationType.Bin, IsActive = true },
        });

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedContents.Single().LocationId.Should().Be(32);
    }

    [Fact]
    public async Task Handle_DefaultLocationIsNotABin_UsesFirstActiveBin()
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });
        _inventory.Setup(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<StorageLocation>
        {
            new() { Id = 30, Name = "Warehouse", LocationType = LocationType.Area, IsActive = true, IsDefault = true },
            new() { Id = 32, Name = "B-1", LocationType = LocationType.Bin, IsActive = true },
        });

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _addedContents.Single().LocationId.Should().Be(32);
    }

    [Fact]
    public async Task Handle_NoActiveBins_StocksIntoTheProvisionedDefaultBin()
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });
        _inventory.Setup(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<StorageLocation>());
        _inventory.Setup(i => i.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 40, Name = "Main", LocationType = LocationType.Bin, IsActive = true, IsDefault = true });

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _inventory.Verify(i => i.AddLocationAsync(It.IsAny<StorageLocation>(), It.IsAny<CancellationToken>()), Times.Never);
        _addedContents.Single().LocationId.Should().Be(40);
    }

    [Fact]
    public async Task Handle_NoActiveBinsAndDefaultIsNotABin_ProvisionsReceivingBin()
    {
        var po = GivenStockPo(5m, new Part { Id = 10, PartNumber = "P-10" });
        _inventory.Setup(i => i.GetStorageLocationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<StorageLocation>
        {
            new() { Id = 31, Name = "Retired", LocationType = LocationType.Bin, IsActive = false },
            new() { Id = 30, Name = "Warehouse", LocationType = LocationType.Area, IsActive = true, IsDefault = true },
        });
        _inventory.Setup(i => i.EnsureDefaultLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageLocation { Id = 30, Name = "Warehouse", LocationType = LocationType.Area, IsActive = true, IsDefault = true });
        _inventory.Setup(i => i.AddLocationAsync(It.IsAny<StorageLocation>(), It.IsAny<CancellationToken>()))
            .Callback<StorageLocation, CancellationToken>((l, _) => l.Id = 50)
            .Returns(Task.CompletedTask);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        _inventory.Verify(i => i.AddLocationAsync(
            It.Is<StorageLocation>(l => l.Name == "Receiving" && l.LocationType == LocationType.Bin && l.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
        _addedContents.Single().LocationId.Should().Be(50);
    }

    [Fact]
    public async Task Handle_DifferentLot_CreatesSeparateContentInsteadOfMerging()
    {
        var po = GivenStockPo(10m, new Part { Id = 10, PartNumber = "P-10", DefaultBinId = 7 });
        GivenLocation(7);
        var heatA = new BinContent { Id = 1, LocationId = 7, EntityType = "part", EntityId = 10, Quantity = 5m, LotNumber = "HEAT-A" };
        _inventory.Setup(i => i.FindActiveBinContentByPartLocationLotAsync(10, 7, "HEAT-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(heatA);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 3m, StorageLocationId: null, Notes: null, LotNumber: "HEAT-B") }),
            CancellationToken.None);

        heatA.Quantity.Should().Be(5m);
        _addedContents.Single().LotNumber.Should().Be("HEAT-B");
        _addedContents.Single().Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task Handle_SameLot_IncrementsExistingContent()
    {
        var po = GivenStockPo(10m, new Part { Id = 10, PartNumber = "P-10", DefaultBinId = 7 });
        GivenLocation(7);
        var heatA = new BinContent { Id = 1, LocationId = 7, EntityType = "part", EntityId = 10, Quantity = 5m, LotNumber = "HEAT-A" };
        _inventory.Setup(i => i.FindActiveBinContentByPartLocationLotAsync(10, 7, "HEAT-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(heatA);

        await _stockingHandler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 3m, StorageLocationId: null, Notes: null, LotNumber: " HEAT-A ") }),
            CancellationToken.None);

        heatA.Quantity.Should().Be(8m);
        _addedContents.Should().BeEmpty();
        _addedMovements.Single().LotNumber.Should().Be("HEAT-A");
    }

    [Fact]
    public async Task Handle_ByWeight_WeighsBaseUnitsNotPurchaseUnits()
    {
        var po = PoWith(estimatedFreight: null,
            (1, 10, qty: 2m, unitPrice: 10m),
            (2, 11, qty: 24m, unitPrice: 10m));
        po.Lines.First().Part = new Part { Id = 10, PartNumber = "BAR", WeightEach = 100m };
        po.Lines.First().PurchaseUnit = new PartPurchaseUnit { Id = 3, PartId = 10, Label = "12 ft bar", ContentQuantity = 12m };
        po.Lines.ElementAt(1).Part = new Part { Id = 11, PartNumber = "LOOSE", WeightEach = 100m };
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 24m, StorageLocationId: null, Notes: null),
            },
            ActualFreight: 100m,
            FreightAllocationMethod: FreightAllocationMethod.ByWeight), CancellationToken.None);

        _addedRecords[0].AllocatedFreight.Should().Be(50m);
        _addedRecords[1].AllocatedFreight.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_WritesOneActivityRowOnThePurchaseOrder()
    {
        var po = PoWith(estimatedFreight: null, (1, 10, qty: 5m, unitPrice: 10m), (2, 11, qty: 5m, unitPrice: 10m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        using var db = Forge.Tests.Helpers.TestDbContextFactory.Create();
        var handler = new ReceiveItemsHandler(_repo.Object, _clock, _mediator.Object, _httpContext.Object, db);

        await handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null, LotNumber: "HEAT-A"),
                new(LineId: 2, Quantity: 1m, StorageLocationId: null, Notes: null),
            }), CancellationToken.None);

        var row = db.ActivityLogs.Local.Should().ContainSingle().Subject;
        row.EntityType.Should().Be("PurchaseOrder");
        row.EntityId.Should().Be(po.Id);
        row.Action.Should().Be("items-received");
        row.Description.Should().StartWith("Received 2 lines on receipt R-").And.EndWith("(lot HEAT-A)");
    }

    [Fact]
    public async Task Handle_PackingSlip_IsStampedOnEveryRecordAndNamedInTheActivityRow()
    {
        var po = PoWith(estimatedFreight: null, (1, 10, qty: 5m, unitPrice: 10m), (2, 11, qty: 5m, unitPrice: 10m));
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        using var db = Forge.Tests.Helpers.TestDbContextFactory.Create();
        var handler = new ReceiveItemsHandler(_repo.Object, _clock, _mediator.Object, _httpContext.Object, db);

        await handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel>
            {
                new(LineId: 1, Quantity: 2m, StorageLocationId: null, Notes: null),
                new(LineId: 2, Quantity: 1m, StorageLocationId: null, Notes: null),
            },
            PackingSlipNumber: "  PS-4471  "), CancellationToken.None);

        _addedRecords.Should().HaveCount(2).And.OnlyContain(r => r.PackingSlipNumber == "PS-4471");
        db.ActivityLogs.Local.Should().ContainSingle().Which.Description
            .Should().StartWith("Received 2 lines on receipt R-").And.EndWith(", packing slip PS-4471");
    }

    [Fact]
    public async Task Handle_SubmittedPurchaseOrder_CanBeReceivedWithoutAPackingSlip()
    {
        var po = PoWith(estimatedFreight: null, (1, 10, qty: 5m, unitPrice: 10m));
        po.Status = PurchaseOrderStatus.Submitted;
        _repo.Setup(r => r.FindWithDetailsAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new ReceiveItemsCommand(
            po.Id,
            new List<ReceiveLineModel> { new(LineId: 1, Quantity: 5m, StorageLocationId: null, Notes: null) }),
            CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Received);
        _addedRecords.Should().ContainSingle().Which.PackingSlipNumber.Should().BeNull();
    }
}
