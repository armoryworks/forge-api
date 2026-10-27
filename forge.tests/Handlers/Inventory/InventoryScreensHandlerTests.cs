using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Lots;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Serials;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Handlers.Inventory;

public sealed class InventoryScreensHandlerTests : IDisposable
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly FixedClock _clock = new();

    public void Dispose() => _db.Dispose();

    private async Task<InterPlantTransfer> SeedTransferAsync(InterPlantTransferStatus status, int fromBin, int toBin, int partId)
    {
        var transfer = new InterPlantTransfer
        {
            TransferNumber = $"IPT-20261026-{Guid.NewGuid():N}"[..20],
            FromPlantId = 1,
            ToPlantId = 2,
            Status = status,
            Lines = [new InterPlantTransferLine { PartId = partId, Quantity = 40m, FromLocationId = fromBin, ToLocationId = toBin }],
        };
        _db.InterPlantTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        return transfer;
    }

    private async Task<(Part Part, StorageLocation Source, StorageLocation Destination, StorageLocation Overflow)> SeedStockAsync()
    {
        _db.Plants.AddRange(new Plant { Id = 1, Code = "N", Name = "North" }, new Plant { Id = 2, Code = "S", Name = "South" });
        var part = new Part { PartNumber = "BRK-10", Description = "Bracket" };
        var source = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        var destination = new StorageLocation { Name = "S1", LocationType = LocationType.Bin, IsActive = true };
        var overflow = new StorageLocation { Name = "S9", LocationType = LocationType.Bin, IsActive = true };
        _db.Parts.Add(part);
        _db.StorageLocations.AddRange(source, destination, overflow);
        await _db.SaveChangesAsync();
        _db.BinContents.Add(new BinContent
        {
            LocationId = source.Id, EntityType = "part", EntityId = part.Id, Quantity = 100m,
            Status = BinContentStatus.Stored, PlacedAt = _clock.UtcNow.AddDays(-2),
        });
        await _db.SaveChangesAsync();
        return (part, source, destination, overflow);
    }

    [Theory]
    [InlineData(InterPlantTransferStatus.Draft)]
    [InlineData(InterPlantTransferStatus.Approved)]
    public async Task Cancel_BeforeShipping_CancelsTheTransfer(InterPlantTransferStatus status)
    {
        var (part, source, destination, _) = await SeedStockAsync();
        var transfer = await SeedTransferAsync(status, source.Id, destination.Id, part.Id);

        await new CancelInterPlantTransferHandler(_db).Handle(new CancelInterPlantTransferCommand(transfer.Id), CancellationToken.None);

        (await _db.InterPlantTransfers.SingleAsync()).Status.Should().Be(InterPlantTransferStatus.Cancelled);
        _db.ActivityLogs.Should().Contain(a => a.EntityType == "InterPlantTransfer" && a.EntityId == transfer.Id && a.Action == "cancelled");
    }

    [Theory]
    [InlineData(InterPlantTransferStatus.Shipped, "*has shipped*")]
    [InlineData(InterPlantTransferStatus.Received, "*already been received*")]
    [InlineData(InterPlantTransferStatus.Cancelled, "*already cancelled*")]
    public async Task Cancel_AfterShipping_IsRefused(InterPlantTransferStatus status, string message)
    {
        var (part, source, destination, _) = await SeedStockAsync();
        var transfer = await SeedTransferAsync(status, source.Id, destination.Id, part.Id);

        var act = () => new CancelInterPlantTransferHandler(_db).Handle(new CancelInterPlantTransferCommand(transfer.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        (await _db.InterPlantTransfers.SingleAsync()).Status.Should().Be(status);
    }

    [Fact]
    public async Task Receive_IntoAChosenBin_PutsTheStockThere()
    {
        var (part, source, destination, overflow) = await SeedStockAsync();
        var transfer = await SeedTransferAsync(InterPlantTransferStatus.Draft, source.Id, destination.Id, part.Id);
        await new ShipInterPlantTransferHandler(_db, _clock, UserAccessor(7))
            .Handle(new ShipInterPlantTransferCommand(transfer.Id, new ShipTransferRequestModel(null)), CancellationToken.None);

        await new ReceiveInterPlantTransferHandler(_db, _clock, UserAccessor(7))
            .Handle(new ReceiveInterPlantTransferCommand(transfer.Id, [new ReceiveTransferLineRequestModel(part.Id, 40m, overflow.Id)]), CancellationToken.None);

        (await _db.BinContents.Where(b => b.LocationId == overflow.Id && b.RemovedAt == null).SumAsync(b => b.Quantity)).Should().Be(40m);
        (await _db.BinContents.Where(b => b.LocationId == destination.Id && b.RemovedAt == null).SumAsync(b => b.Quantity)).Should().Be(0m);
        (await _db.InterPlantTransferLines.SingleAsync()).ToLocationId.Should().Be(overflow.Id);
    }

    [Fact]
    public async Task TransferList_ShowsEachLinesBins()
    {
        var (part, source, destination, _) = await SeedStockAsync();
        await SeedTransferAsync(InterPlantTransferStatus.Draft, source.Id, destination.Id, part.Id);

        var transfers = await new GetInterPlantTransfersHandler(_db).Handle(new GetInterPlantTransfersQuery(), CancellationToken.None);

        var line = transfers.Single().Lines.Single();
        (line.PartNumber, line.FromLocationPath, line.ToLocationPath).Should().Be(("BRK-10", "A1", "S1"));
        transfers.Single().FromPlantName.Should().Be("North");
    }

    [Fact]
    public async Task TransferPlants_ListOnlyActivePlants_DefaultFirst()
    {
        _db.Plants.AddRange(
            new Plant { Code = "B", Name = "Branch" },
            new Plant { Code = "Z", Name = "Main", IsDefault = true },
            new Plant { Code = "X", Name = "Closed", IsActive = false });
        await _db.SaveChangesAsync();

        var plants = await new GetInterPlantTransferPlantsHandler(_db).Handle(new GetInterPlantTransferPlantsQuery(), CancellationToken.None);

        plants.Select(p => p.Name).Should().Equal("Main", "Branch");
    }

    private async Task<(Part Part, BinContent Bin, SalesOrderLine Line)> SeedSalesOrderAsync(
        SalesOrderStatus status = SalesOrderStatus.Confirmed, decimal ordered = 30m, decimal shipped = 0m)
    {
        var part = new Part { PartNumber = "VLV-2", Description = "Valve" };
        var customer = new Customer { Name = "Buyer", CompanyName = "Buyer Co" };
        var location = new StorageLocation { Name = "R1", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(part, customer, location);
        await _db.SaveChangesAsync();
        var so = new SalesOrder { OrderNumber = "SO-1001", CustomerId = customer.Id, Status = status };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = so.Id, PartId = part.Id, Description = "Valve", Quantity = ordered, ShippedQuantity = shipped, UnitPrice = 5m, LineNumber = 1 };
        var bin = new BinContent
        {
            LocationId = location.Id, Location = location, EntityType = "part", EntityId = part.Id, Quantity = 100m,
            Status = BinContentStatus.Stored, PlacedAt = _clock.UtcNow,
        };
        _db.SalesOrderLines.Add(line);
        _db.BinContents.Add(bin);
        await _db.SaveChangesAsync();
        return (part, bin, line);
    }

    private Task<ReservationResponseModel> ReserveAsync(int partId, BinContent bin, int? jobId, int? lineId, decimal quantity)
    {
        var repo = new Mock<IInventoryRepository>();
        repo.Setup(r => r.FindBinContentWithLocationAsync(bin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bin);
        repo.Setup(r => r.AddReservationAsync(It.IsAny<Reservation>(), It.IsAny<CancellationToken>()))
            .Returns<Reservation, CancellationToken>((r, ct) => _db.Reservations.AddAsync(r, ct).AsTask());
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns<CancellationToken>(ct => _db.SaveChangesAsync(ct));
        return new CreateReservationHandler(repo.Object, _db)
            .Handle(new CreateReservationCommand(new CreateReservationRequestModel(partId, bin.Id, jobId, lineId, quantity, null)), CancellationToken.None);
    }

    [Fact]
    public async Task Reserve_ForASalesOrderLine_NamesTheOrder()
    {
        var (part, bin, line) = await SeedSalesOrderAsync();

        var reservation = await ReserveAsync(part.Id, bin, null, line.Id, 12m);

        (reservation.SalesOrderLineId, reservation.SalesOrderNumber, reservation.SalesOrderLineNumber).Should().Be((line.Id, "SO-1001", 1));
        bin.ReservedQuantity.Should().Be(12m);
    }

    [Fact]
    public async Task Reserve_MoreThanTheLineStillNeeds_IsRefused()
    {
        var (part, bin, line) = await SeedSalesOrderAsync(ordered: 30m, shipped: 10m);
        await ReserveAsync(part.Id, bin, null, line.Id, 15m);

        var act = () => ReserveAsync(part.Id, bin, null, line.Id, 6m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only 5 is still open and unreserved*");
    }

    [Fact]
    public async Task Reserve_ALineOfAnotherPart_IsRefused()
    {
        var (_, bin, line) = await SeedSalesOrderAsync();
        var other = new Part { PartNumber = "OTHER", Description = "Other" };
        _db.Parts.Add(other);
        await _db.SaveChangesAsync();
        bin.EntityId = other.Id;

        var act = () => ReserveAsync(other.Id, bin, null, line.Id, 1m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different part*");
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Completed)]
    public async Task Reserve_ForALineOfAClosedOrDraftOrder_IsRefused(SalesOrderStatus status)
    {
        var (part, bin, line) = await SeedSalesOrderAsync(status);

        var act = () => ReserveAsync(part.Id, bin, null, line.Id, 1m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not open*");
    }

    [Fact]
    public async Task Reserve_FromABinOfAnotherPart_IsRefused()
    {
        var (part, bin, _) = await SeedSalesOrderAsync();
        bin.EntityId = part.Id + 100;

        var act = () => ReserveAsync(part.Id, bin, null, null, 1m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not hold this part*");
    }

    [Fact]
    public void Reserve_ForAJobAndALineAtOnce_FailsValidation()
    {
        var result = new CreateReservationCommandValidator().Validate(
            new CreateReservationCommand(new CreateReservationRequestModel(1, 1, 3, 4, 1m, null)));

        result.Errors.Should().ContainSingle(e => e.ErrorCode == "reservation-one-demand");
    }

    [Fact]
    public async Task ReservableLines_ShowOpenAndReservedQuantity()
    {
        var (part, bin, line) = await SeedSalesOrderAsync(ordered: 30m, shipped: 10m);
        await ReserveAsync(part.Id, bin, null, line.Id, 8m);

        var lines = await new GetReservableSalesOrderLinesHandler(_db)
            .Handle(new GetReservableSalesOrderLinesQuery(part.Id, null), CancellationToken.None);

        var only = lines.Should().ContainSingle().Subject;
        (only.SalesOrderNumber, only.CustomerName, only.OpenQuantity, only.ReservedQuantity).Should().Be(("SO-1001", "Buyer Co", 20m, 8m));
    }

    [Fact]
    public async Task Reservations_FilteredBySalesOrder_ReturnOnlyThatOrdersLines()
    {
        var (part, bin, line) = await SeedSalesOrderAsync();
        await ReserveAsync(part.Id, bin, null, line.Id, 4m);
        await ReserveAsync(part.Id, bin, null, null, 2m);

        var repo = new InventoryRepository(_db);
        var forOrder = await repo.GetReservationsAsync(null, null, line.SalesOrderId, null, CancellationToken.None);

        forOrder.Should().ContainSingle().Which.SalesOrderNumber.Should().Be("SO-1001");
    }

    [Fact]
    public async Task Atp_ExcludingTheLineBeingChecked_DoesNotCountItsOwnDemand()
    {
        var (part, _, line) = await SeedSalesOrderAsync(ordered: 90m);
        var atp = new AtpService(_db, _clock);

        var counted = await atp.CalculateAtpAsync(part.Id, 90m);
        var excluded = await atp.CalculateAtpAsync(part.Id, 90m, line.Id);

        (counted.AvailableToPromise, counted.CanFulfill).Should().Be((10m, false));
        (excluded.AvailableToPromise, excluded.CanFulfill).Should().Be((100m, true));
    }

    [Fact]
    public async Task AbcResults_CarryThePartsCurrentClass()
    {
        var part = new Part { PartNumber = "ABC-1", Description = "Gear", AbcClass = AbcClass.C };
        _db.Parts.Add(part);
        var run = new AbcClassificationRun { RunDate = _clock.UtcNow, TotalParts = 1 };
        _db.AbcClassificationRuns.Add(run);
        await _db.SaveChangesAsync();
        _db.AbcClassifications.Add(new AbcClassification { PartId = part.Id, RunId = run.Id, Classification = AbcClass.A, Rank = 1, CalculatedAt = _clock.UtcNow });
        await _db.SaveChangesAsync();

        var results = await new GetAbcClassificationResultsHandler(_db).Handle(new GetAbcClassificationResultsQuery(run.Id), CancellationToken.None);

        (results.Single().Classification, results.Single().CurrentClassification).Should().Be((AbcClass.A, AbcClass.C));
    }

    [Theory]
    [InlineData(SerialNumberStatus.Shipped)]
    [InlineData(SerialNumberStatus.Scrapped)]
    public async Task SerialTransfer_OfASerialNoLongerInStock_IsRefused(SerialNumberStatus status)
    {
        var part = new Part { PartNumber = "SN-P", Description = "Pump" };
        var bin = new StorageLocation { Name = "Q1", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(part, bin);
        await _db.SaveChangesAsync();
        var serial = new SerialNumber { PartId = part.Id, SerialValue = "SN-0001", Status = status };
        _db.SerialNumbers.Add(serial);
        await _db.SaveChangesAsync();

        var act = () => new TransferSerialHandler(_db, _clock).Handle(
            new TransferSerialCommand(serial.Id, new TransferSerialRequestModel { ToLocationId = bin.Id }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer in stock*");
    }

    [Fact]
    public async Task SerialTransfer_MovesTheSerialAndRecordsTheReason()
    {
        var part = new Part { PartNumber = "SN-P", Description = "Pump" };
        var from = new StorageLocation { Name = "Q1", LocationType = LocationType.Bin, IsActive = true };
        var to = new StorageLocation { Name = "Q2", LocationType = LocationType.Bin, IsActive = true };
        _db.AddRange(part, from, to);
        await _db.SaveChangesAsync();
        var serial = new SerialNumber { PartId = part.Id, SerialValue = "SN-0002", CurrentLocationId = from.Id };
        _db.SerialNumbers.Add(serial);
        await _db.SaveChangesAsync();

        await new TransferSerialHandler(_db, _clock).Handle(
            new TransferSerialCommand(serial.Id, new TransferSerialRequestModel { ToLocationId = to.Id, Notes = "Rework cell" }), CancellationToken.None);

        (await _db.SerialNumbers.SingleAsync()).CurrentLocationId.Should().Be(to.Id);
        var history = await _db.Set<SerialHistory>().SingleAsync();
        (history.FromLocationName, history.ToLocationName, history.Details).Should().Be(("Q1", "Q2", "Rework cell"));
    }

    [Fact]
    public void ReceiptExpiry_DefaultsFromShelfLife_WhenNoneIsGiven()
    {
        var part = new Part { ShelfLifeDays = 180 };
        var received = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
        var given = new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero);

        ReceiptLotRecords.ExpirationFor(part, null, received).Should().Be(new DateTimeOffset(2027, 4, 24, 0, 0, 0, TimeSpan.Zero));
        ReceiptLotRecords.ExpirationFor(part, given, received).Should().Be(given);
        ReceiptLotRecords.ExpirationFor(new Part(), null, received).Should().BeNull();
    }

    [Fact]
    public async Task ReceiveItems_StoresTheLotExpiry_AndKeepsTheEarlierOneOnALaterReceipt()
    {
        var vendor = new Vendor { CompanyName = "Resin supply" };
        var bin = new StorageLocation { Name = "RC-2", LocationType = LocationType.Bin, IsActive = true };
        var part = new Part { PartNumber = "RES-1", Description = "Resin", TraceabilityType = TraceabilityType.Lot };
        _db.AddRange(vendor, bin, part);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-EXP-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, OrderedQuantity = 100m, UnitPrice = 2m };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var later = new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2027, 3, 31, 0, 0, 0, TimeSpan.Zero);

        await ReceiveAsync(po.Id, new ReceiveLineModel(line.Id, 10m, bin.Id, null, LotNumber: "R-77", ExpirationDate: later));
        _db.ChangeTracker.Clear();
        await ReceiveAsync(po.Id, new ReceiveLineModel(line.Id, 10m, bin.Id, null, LotNumber: "R-77", ExpirationDate: earlier));

        var lot = await _db.LotRecords.SingleAsync();
        (lot.LotNumber, lot.Quantity, lot.ExpirationDate).Should().Be(("R-77", 20m, earlier));
    }

    private Task ReceiveAsync(int poId, ReceiveLineModel line)
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "Test")),
        });
        return new ReceiveItemsHandler(
                new PurchaseOrderRepository(_db), _clock, Mock.Of<MediatR.IMediator>(), http.Object,
                _db, null, new InventoryRepository(_db), new StubCapabilitySnapshotProvider(ReceiptLotRecords.Capability),
                Mock.Of<IBarcodeService>())
            .Handle(new ReceiveItemsCommand(poId, [line]), CancellationToken.None);
    }

    [Fact]
    public async Task LotTrace_CarriesTheLotNotes()
    {
        var part = new Part { PartNumber = "LN-1", Description = "Film" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        _db.LotRecords.Add(new LotRecord { LotNumber = "LN-LOT", PartId = part.Id, Quantity = 1m, Notes = "Store below 25 C" });
        await _db.SaveChangesAsync();

        var trace = await new GetLotTraceabilityHandler(_db).Handle(new GetLotTraceabilityQuery("LN-LOT"), CancellationToken.None);

        trace.Notes.Should().Be("Store below 25 C");
    }
}
