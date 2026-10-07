using System.Security.Claims;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Lots;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// CAP-QC-RECALL — initiating a recall walks the lot_consumptions genealogy forward to the
/// full blast radius, quarantines matching on-hand (Stored → QcHold), resolves the
/// shipments/customers that received affected lots from the lot-stamped Ship movements,
/// and freezes an immutable snapshot.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InitiateRecallHandlerTests(PostgresFixture fixture)
{
    private static IHttpContextAccessor Http(int userId = 1) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        },
    };

    private static async Task<Part> SeedPartAsync(AppDbContext db)
    {
        var part = new Part
        {
            PartNumber = $"RC-{Guid.NewGuid():N}"[..16],
            Name = "Recall Test",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    private static async Task<LotRecord> SeedLotAsync(AppDbContext db, int partId, decimal qty, int? jobId = null)
    {
        var lot = new LotRecord
        {
            LotNumber = $"LOT-{Guid.NewGuid():N}"[..20], PartId = partId, Quantity = qty, JobId = jobId,
        };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync();
        return lot;
    }

    private static Task Consume(AppDbContext db, int producedId, int consumedId, decimal qty) =>
        new RecordLotConsumptionHandler(db).Handle(
            new RecordLotConsumptionCommand(producedId,
                new RecordLotConsumptionRequestModel([new LotConsumptionInputModel(consumedId, qty)])),
            CancellationToken.None);

    private sealed record ShippedLine(Customer Customer, SalesOrderLine SoLine, Shipment Shipment, ShipmentLine Line);

    private static async Task<ShippedLine> SeedShipmentAsync(AppDbContext db, string customerName, decimal qty)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer { Name = customerName };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder { CustomerId = customer.Id, OrderNumber = $"SO-{suffix}" };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var soLine = new SalesOrderLine { SalesOrderId = so.Id, Description = "Bracket", Quantity = qty, UnitPrice = 5, LineNumber = 1 };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = $"SHP-{suffix}", SalesOrderId = so.Id, TrackingNumber = "1Z999",
            ShippedDate = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        var line = new ShipmentLine { ShipmentId = shipment.Id, SalesOrderLineId = soLine.Id, Quantity = qty };
        db.ShipmentLines.Add(line);
        await db.SaveChangesAsync();
        return new ShippedLine(customer, soLine, shipment, line);
    }

    private static async Task<BinMovement> ShipAsync(AppDbContext db, ShipmentLine line, string? lotNumber, decimal qty)
    {
        var movement = new BinMovement
        {
            EntityType = "ShipmentLine", EntityId = line.Id, Quantity = -qty, LotNumber = lotNumber,
            Reason = BinMovementReason.Ship, MovedBy = 1, MovedAt = DateTimeOffset.UtcNow,
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync();
        return movement;
    }

    private static async Task<Job> SeedJobAsync(AppDbContext db, int? soLineId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var track = new TrackType { Name = "Production", Code = $"P{suffix}", SortOrder = 1 };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Done", Code = "DONE", SortOrder = 1 };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        var job = new Job
        {
            JobNumber = $"JOB-{suffix}", Title = "Bracket run",
            TrackTypeId = track.Id, CurrentStageId = stage.Id, SalesOrderLineId = soLineId,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static InitiateRecallCommand Recall(int lotId) =>
        new(new InitiateRecallRequestModel(lotId, "Supplier contamination", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Forward_traces_the_blast_radius_and_snapshots_it()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var produced = await SeedLotAsync(db, part.Id, 40);
        await Consume(db, produced.Id, raw.Id, 60);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(raw.Id), CancellationToken.None);

        result.Status.Should().Be(RecallStatus.Active);
        result.AffectedLotsCount.Should().Be(2);
        result.AffectedLots.Select(l => l.LotNumber)
            .Should().BeEquivalentTo(new[] { raw.LotNumber, produced.LotNumber });
    }

    [Fact]
    public async Task Forward_trace_is_multi_level()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var mid = await SeedLotAsync(db, part.Id, 50);
        var final = await SeedLotAsync(db, part.Id, 20);
        await Consume(db, mid.Id, raw.Id, 60);
        await Consume(db, final.Id, mid.Id, 30);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(raw.Id), CancellationToken.None);

        result.AffectedLotsCount.Should().Be(3);
        result.AffectedLots.Select(l => l.LotNumber)
            .Should().BeEquivalentTo(new[] { raw.LotNumber, mid.LotNumber, final.LotNumber });
    }

    [Fact]
    public async Task Quarantines_matching_on_hand_to_qc_hold()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var produced = await SeedLotAsync(db, part.Id, 40);
        await Consume(db, produced.Id, raw.Id, 60);

        var loc = new StorageLocation { Name = "Bin R1", LocationType = LocationType.Bin };
        db.StorageLocations.Add(loc);
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent
        {
            LocationId = loc.Id, EntityType = "part", EntityId = part.Id,
            Quantity = 40, LotNumber = produced.LotNumber, Status = BinContentStatus.Stored,
        });
        await db.SaveChangesAsync();

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(raw.Id), CancellationToken.None);

        result.TotalQuarantinedQuantity.Should().Be(40);
        await using var verify = fixture.CreateContext();
        var bc = await verify.BinContents.FirstAsync(b => b.LotNumber == produced.LotNumber);
        bc.Status.Should().Be(BinContentStatus.QcHold);
    }

    [Fact]
    public async Task Resolves_affected_shipments_and_customers()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var shipped = await SeedShipmentAsync(db, "Acme Aerospace", 10);
        var job = await SeedJobAsync(db, shipped.SoLine.Id);
        var produced = await SeedLotAsync(db, part.Id, 10, jobId: job.Id);
        await ShipAsync(db, shipped.Line, produced.LotNumber, 10);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(produced.Id), CancellationToken.None);

        result.AffectedShipmentsCount.Should().Be(1);
        var shp = result.AffectedShipments.Single();
        shp.CustomerId.Should().Be(shipped.Customer.Id);
        shp.CustomerName.Should().Be("Acme Aerospace");
        shp.ShipmentNumber.Should().Be(shipped.Shipment.ShipmentNumber);
        shp.AffectedQuantity.Should().Be(10);
        shp.IsApproximate.Should().BeFalse();
    }

    [Fact]
    public async Task Stock_built_lot_lists_the_customers_it_actually_shipped_to()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var stockJob = await SeedJobAsync(db, soLineId: null);
        var recalled = await SeedLotAsync(db, part.Id, 30, jobId: stockJob.Id);
        var other = await SeedLotAsync(db, part.Id, 30);

        var first = await SeedShipmentAsync(db, "Stock Buyer One", 8);
        var second = await SeedShipmentAsync(db, "Stock Buyer Two", 5);
        var unrelated = await SeedShipmentAsync(db, "Other Lot Buyer", 7);
        await ShipAsync(db, first.Line, recalled.LotNumber, 8);
        await ShipAsync(db, second.Line, recalled.LotNumber, 3);
        await ShipAsync(db, second.Line, other.LotNumber, 2);
        await ShipAsync(db, unrelated.Line, other.LotNumber, 7);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(recalled.Id), CancellationToken.None);

        result.AffectedShipments.Select(s => (s.CustomerName, s.AffectedQuantity))
            .Should().BeEquivalentTo(new[] { ("Stock Buyer One", 8m), ("Stock Buyer Two", 3m) });
        result.AffectedShipments.Should().OnlyContain(s => !s.IsApproximate);
    }

    [Fact]
    public async Task Affected_quantity_is_net_of_reversed_ship_movements()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var lot = await SeedLotAsync(db, part.Id, 20);
        var partial = await SeedShipmentAsync(db, "Partly Returned", 10);
        var undone = await SeedShipmentAsync(db, "Fully Reversed", 4);
        var partialMove = await ShipAsync(db, partial.Line, lot.LotNumber, 10);
        var undoneMove = await ShipAsync(db, undone.Line, lot.LotNumber, 4);
        db.BinMovements.AddRange(
            new BinMovement
            {
                EntityType = "part", EntityId = part.Id, Quantity = 4, Reason = BinMovementReason.Reversal,
                ReversedMovementId = partialMove.Id, MovedBy = 1, MovedAt = DateTimeOffset.UtcNow,
            },
            new BinMovement
            {
                EntityType = "part", EntityId = part.Id, Quantity = 4, Reason = BinMovementReason.Reversal,
                ReversedMovementId = undoneMove.Id, MovedBy = 1, MovedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(lot.Id), CancellationToken.None);

        result.AffectedShipments.Should().ContainSingle();
        result.AffectedShipments.Single().CustomerName.Should().Be("Partly Returned");
        result.AffectedShipments.Single().AffectedQuantity.Should().Be(6);
    }

    [Fact]
    public async Task Unlotted_ship_movement_falls_back_to_the_sales_order_line_as_approximate()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var orderLine = await SeedShipmentAsync(db, "Unlotted Buyer", 6);
        var lottedElsewhere = await SeedShipmentAsync(db, "Lotted Elsewhere", 6);
        var job = await SeedJobAsync(db, orderLine.SoLine.Id);
        var produced = await SeedLotAsync(db, part.Id, 6, jobId: job.Id);
        var other = await SeedLotAsync(db, part.Id, 6);
        await ShipAsync(db, orderLine.Line, null, 6);
        db.ShipmentLines.Add(new ShipmentLine { ShipmentId = lottedElsewhere.Shipment.Id, SalesOrderLineId = orderLine.SoLine.Id, Quantity = 6 });
        await db.SaveChangesAsync();
        var lottedLine = await db.ShipmentLines.OrderByDescending(l => l.Id).FirstAsync();
        await ShipAsync(db, lottedLine, other.LotNumber, 6);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(produced.Id), CancellationToken.None);

        var shp = result.AffectedShipments.Should().ContainSingle().Subject;
        shp.CustomerName.Should().Be("Unlotted Buyer");
        shp.AffectedQuantity.Should().Be(6);
        shp.IsApproximate.Should().BeTrue();
    }

    [Fact]
    public async Task A_soft_deleted_lot_in_the_genealogy_is_still_recalled()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var produced = await SeedLotAsync(db, part.Id, 40);
        await Consume(db, produced.Id, raw.Id, 60);
        produced.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(raw.Id), CancellationToken.None);

        result.AffectedLots.Select(l => l.LotNumber)
            .Should().BeEquivalentTo(new[] { raw.LotNumber, produced.LotNumber });
    }

    [Fact]
    public async Task Resolve_marks_the_recall_resolved()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var created = await new InitiateRecallHandler(db, Http()).Handle(Recall(raw.Id), CancellationToken.None);

        var resolved = await new ResolveRecallHandler(db)
            .Handle(new ResolveRecallCommand(created.Id, new ResolveRecallRequestModel("Contained; scrapped affected lots")),
                CancellationToken.None);

        resolved.Status.Should().Be(RecallStatus.Resolved);
        resolved.ResolvedAt.Should().NotBeNull();
        resolved.ResolutionNotes.Should().Be("Contained; scrapped affected lots");
    }
}
