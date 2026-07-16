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
/// shipments/customers that received affected lots, and freezes an immutable snapshot.
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

        var customer = new Customer { Name = "Acme Aerospace" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder { CustomerId = customer.Id, OrderNumber = "SO-REC-1" };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();
        var soLine = new SalesOrderLine { SalesOrderId = so.Id, Description = "Bracket", Quantity = 10, UnitPrice = 5, LineNumber = 1 };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var track = new TrackType { Name = "Production", Code = "PROD", SortOrder = 1 };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Done", Code = "DONE", SortOrder = 1 };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();

        var job = new Job
        {
            JobNumber = "JOB-REC-1", Title = "Bracket run",
            TrackTypeId = track.Id, CurrentStageId = stage.Id, SalesOrderLineId = soLine.Id,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var shipment = new Shipment { ShipmentNumber = "SHP-REC-1", SalesOrderId = so.Id, TrackingNumber = "1Z999" };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        db.ShipmentLines.Add(new ShipmentLine { ShipmentId = shipment.Id, SalesOrderLineId = soLine.Id, Quantity = 10 });
        await db.SaveChangesAsync();

        var produced = await SeedLotAsync(db, part.Id, 10, jobId: job.Id);

        var result = await new InitiateRecallHandler(db, Http()).Handle(Recall(produced.Id), CancellationToken.None);

        result.AffectedShipmentsCount.Should().Be(1);
        var shp = result.AffectedShipments.Single();
        shp.CustomerId.Should().Be(customer.Id);
        shp.CustomerName.Should().Be("Acme Aerospace");
        shp.ShipmentNumber.Should().Be("SHP-REC-1");
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
