using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

/// <summary>
/// BE-1 / F-030 acceptance tests for InventoryReliefService.
///
/// These tests exercise the service in isolation (no trigger wiring yet — trigger point
/// pending domain specialist ruling per eng-lead Wave-1 queue). They prove:
///   - INV-SH2: each shipment line gets exactly one relief pass
///   - INV-INV1a: bins never go negative
///   - Idempotency: double-call is a safe no-op
///   - FIFO ordering across bins
///   - Insufficient-stock throws (never-negative guard)
/// </summary>
public class InventoryReliefServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly InventoryReliefService _svc;

    public InventoryReliefServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _svc = new InventoryReliefService(_db, NullLogger<InventoryReliefService>.Instance);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(Customer customer, SalesOrder so, Part part)> SeedSpineAsync(
        string partNumber = "P-RELIEF-001",
        decimal orderQty = 20m)
    {
        var customer = new Customer { Name = "Relief Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var part = new Part { PartNumber = partNumber, Name = "Relief Part" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = $"SO-{partNumber}",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines =
            [
                new SalesOrderLine
                {
                    PartId = part.Id,
                    Description = "Relief Part",
                    Quantity = orderQty,
                    UnitPrice = 10m,
                    LineNumber = 1,
                }
            ]
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        return (customer, so, part);
    }

    private BinContent MakeBin(int partId, decimal qty, DateTimeOffset? placedAt = null) => new()
    {
        EntityType = "part",
        EntityId = partId,
        Quantity = qty,
        Status = BinContentStatus.Stored,
        PlacedAt = placedAt ?? DateTimeOffset.UtcNow,
    };

    private static int _shipSeq;
    private Shipment MakeShipment(int soId, int soLineId, decimal shipQty) => new()
    {
        ShipmentNumber = $"SH-TEST-{++_shipSeq:D5}",
        SalesOrderId = soId,
        Status = ShipmentStatus.Pending,
        Lines =
        [
            new ShipmentLine
            {
                SalesOrderLineId = soLineId,
                Quantity = shipQty,
            }
        ]
    };

    // ── INV-SH2: relief exactly once per line ─────────────────────────────────

    [Fact]
    public async Task RelieveShipment_DecrementsExactShipQtyFromBin_INV_SH2()
    {
        // Arrange
        var (_, so, part) = await SeedSpineAsync();
        var soLine = so.Lines.First();

        var bin = MakeBin(part.Id, 20m);
        _db.BinContents.Add(bin);
        await _db.SaveChangesAsync();

        var shipment = MakeShipment(so.Id, soLine.Id, 5m);
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        // Load with navigation properties as the real caller must
        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        // Act
        await _svc.RelieveShipmentAsync(loaded, userId: 1, CancellationToken.None);

        // Assert — bin decreased by exactly ship qty
        _db.ChangeTracker.Clear();
        var binAfter = await _db.BinContents.FindAsync(bin.Id);
        binAfter!.Quantity.Should().Be(15m, "bin must decrease by exactly the shipped quantity");

        // Assert — BinMovement with reason=Ship created
        var shipmentLineId = loaded.Lines.First().Id;
        var movements = await _db.BinMovements
            .Where(bm => bm.Reason == BinMovementReason.Ship
                && bm.EntityType == "part" && bm.EntityId == part.Id && bm.ShipmentLineId == shipmentLineId)
            .ToListAsync();
        movements.Should().HaveCount(1, "exactly one Ship movement per shipment line");
        movements[0].Quantity.Should().Be(5m, "movement quantity is positive, out of the bin's location");
        movements[0].FromLocationId.Should().Be(bin.LocationId);
        movements[0].ToLocationId.Should().BeNull();

        // Assert — idempotency stamp set
        var lineAfter = await _db.Set<ShipmentLine>().FindAsync(loaded.Lines.First().Id);
        lineAfter!.InventoryRelievedAt.Should().NotBeNull("InventoryRelievedAt must be stamped on relief");
    }

    // ── Idempotency: double-call is a no-op ───────────────────────────────────

    [Fact]
    public async Task RelieveShipment_CalledTwice_OnlyRelievesOnce_Idempotent()
    {
        // Arrange
        var (_, so, part) = await SeedSpineAsync("P-IDEMPOTENT-001");
        var soLine = so.Lines.First();

        _db.BinContents.Add(MakeBin(part.Id, 30m));
        await _db.SaveChangesAsync();

        var shipment = MakeShipment(so.Id, soLine.Id, 10m);
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var load = async () => await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        // Act — first call
        await _svc.RelieveShipmentAsync(await load(), userId: 1, CancellationToken.None);
        _db.ChangeTracker.Clear();

        // Act — second call (must be a no-op)
        await _svc.RelieveShipmentAsync(await load(), userId: 1, CancellationToken.None);
        _db.ChangeTracker.Clear();

        // Assert — bin only decremented once (total: 30 - 10 = 20, not 30 - 20 = 10)
        var bins = await _db.BinContents.ToListAsync();
        bins.Sum(b => b.Quantity).Should().Be(20m, "idempotent: second call must not re-decrement");

        var movements = await _db.BinMovements
            .Where(bm => bm.Reason == BinMovementReason.Ship)
            .ToListAsync();
        movements.Should().HaveCount(1, "idempotent: only one Ship movement created");
    }

    // ── FIFO across multiple bins ─────────────────────────────────────────────

    [Fact]
    public async Task RelieveShipment_DrainsBinsInFifoOrder()
    {
        // Arrange — two bins, older one placed first
        var (_, so, part) = await SeedSpineAsync("P-FIFO-001", 30m);
        var soLine = so.Lines.First();

        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        var t1 = DateTimeOffset.UtcNow.AddDays(-1);
        var oldBin = MakeBin(part.Id, 4m, t0);   // older
        var newBin = MakeBin(part.Id, 10m, t1);  // newer

        _db.BinContents.AddRange(oldBin, newBin);
        await _db.SaveChangesAsync();

        // Ship 7 — should exhaust old bin (4) and take 3 from new bin
        var shipment = MakeShipment(so.Id, soLine.Id, 7m);
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        // Act
        await _svc.RelieveShipmentAsync(loaded, userId: 1, CancellationToken.None);

        // Assert — old bin exhausted, new bin partially drained
        _db.ChangeTracker.Clear();
        var ob = await _db.BinContents.FindAsync(oldBin.Id);
        var nb = await _db.BinContents.FindAsync(newBin.Id);
        ob!.Quantity.Should().Be(0m, "old bin exhausted first (FIFO)");
        nb!.Quantity.Should().Be(7m, "new bin partially drained: 10 - 3 = 7");

        var movements = await _db.BinMovements
            .Where(bm => bm.Reason == BinMovementReason.Ship)
            .ToListAsync();
        movements.Should().HaveCount(2, "one movement per bin touched");
    }

    // ── INV-INV1a: never-negative — throws when stock insufficient ───────────

    [Fact]
    public async Task RelieveShipment_InsufficientStock_Throws_NeverNegative()
    {
        // Arrange
        var (_, so, part) = await SeedSpineAsync("P-INSUFF-001", 20m);
        var soLine = so.Lines.First();

        _db.BinContents.Add(MakeBin(part.Id, 3m));   // only 3 in stock
        await _db.SaveChangesAsync();

        var shipment = MakeShipment(so.Id, soLine.Id, 10m);  // wants 10
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        // Act & Assert
        var act = () => _svc.RelieveShipmentAsync(loaded, userId: 1, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Insufficient stock*INV-INV1a*");

        // Assert — bin untouched (transaction rolled back conceptually — in-memory no SaveChanges on throw)
        _db.ChangeTracker.Clear();
        var bin = await _db.BinContents.FirstAsync();
        bin.Quantity.Should().Be(3m, "bin must not be decremented when stock check fails");
    }

    // ── Line with no PartId and no SalesOrderLine — skipped gracefully ────────

    [Fact]
    public async Task RelieveShipment_LineWithNoPartId_SkipsGracefully()
    {
        // Arrange — part-less line (e.g. service/freight charge)
        var customer = new Customer { Name = "NoPartId Test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-NOPART",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines = []
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-NOPART",
            SalesOrderId = so.Id,
            Status = ShipmentStatus.Pending,
            Lines =
            [
                new ShipmentLine
                {
                    PartId = null,
                    SalesOrderLineId = null,
                    Quantity = 1m,
                    Description = "Freight"
                }
            ]
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        // Act — should not throw
        await _svc.RelieveShipmentAsync(loaded, userId: 1, CancellationToken.None);

        // Assert — nothing created
        var movements = await _db.BinMovements.ToListAsync();
        movements.Should().BeEmpty("no bin movement for a part-less line");
    }

    [Fact]
    public async Task RelieveShipment_BinDrawnToZero_IsMarkedRemoved_PartialBinStaysLive()
    {
        var (_, so, part) = await SeedSpineAsync("P-REMOVED-001", 30m);
        var soLine = so.Lines.First();

        var emptied = MakeBin(part.Id, 4m, DateTimeOffset.UtcNow.AddDays(-2));
        var partial = MakeBin(part.Id, 10m, DateTimeOffset.UtcNow.AddDays(-1));
        _db.BinContents.AddRange(emptied, partial);
        await _db.SaveChangesAsync();

        var shipment = MakeShipment(so.Id, soLine.Id, 7m);
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        await _svc.RelieveShipmentAsync(loaded, userId: 7, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var emptiedAfter = await _db.BinContents.FindAsync(emptied.Id);
        var partialAfter = await _db.BinContents.FindAsync(partial.Id);
        emptiedAfter!.RemovedAt.Should().NotBeNull("a bin drawn to zero is marked removed");
        emptiedAfter.RemovedBy.Should().Be(7);
        partialAfter!.RemovedAt.Should().BeNull("a partially drawn bin stays live");
        partialAfter.Quantity.Should().Be(7m);
    }

    [Fact]
    public async Task RelieveShipment_BinHoldingTheLineReservation_IsMarkedRemovedOnceReleased()
    {
        var (_, so, part) = await SeedSpineAsync("P-RESERVED-001", 5m);
        var soLine = so.Lines.First();

        var bin = MakeBin(part.Id, 5m);
        bin.ReservedQuantity = 5m;
        _db.BinContents.Add(bin);
        await _db.SaveChangesAsync();

        _db.Reservations.Add(new Reservation
        {
            PartId = part.Id,
            BinContentId = bin.Id,
            SalesOrderLineId = soLine.Id,
            Quantity = 5m,
        });
        var shipment = MakeShipment(so.Id, soLine.Id, 5m);
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();

        var loaded = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .FirstAsync(s => s.Id == shipment.Id);

        await _svc.RelieveShipmentAsync(loaded, userId: 1, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var binAfter = await _db.BinContents.FindAsync(bin.Id);
        binAfter!.Quantity.Should().Be(0m);
        binAfter.ReservedQuantity.Should().Be(0m);
        binAfter.RemovedAt.Should().NotBeNull("the reservation is released before the empty bin is judged");
    }

    public void Dispose() => _db.Dispose();
}
