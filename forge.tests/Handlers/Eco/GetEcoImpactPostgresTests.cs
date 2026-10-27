using FluentAssertions;

using Forge.Api.Features.Eco;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Eco;

/// <summary>
/// An ECO's impact lists, per affected part, the open jobs that build it or an assembly using it, the open
/// purchase and sales-order lines, and on-hand stock by the lot's revision; finished work is left out.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetEcoImpactPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Lists_open_jobs_orders_and_stock_by_revision_for_the_affected_part()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateContext();

        var track = new TrackType { Name = $"ECO impact {tag}", Code = $"eco-{tag}", IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Machining", Code = "mach", SortOrder = 1, IsActive = true };
        var part = new Part { PartNumber = $"ECO-P-{tag}", Name = "Bracket", Revision = "C" };
        var assembly = new Part { PartNumber = $"ECO-A-{tag}", Name = "Bracket assembly" };
        var vendor = new Vendor { CompanyName = $"Steel {tag}", IsActive = true };
        var customer = new Customer { Name = $"ECO customer {tag}" };
        var location = new StorageLocation { Name = $"ECO-{tag}", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(stage, part, assembly, vendor, customer, location);
        await db.SaveChangesAsync();

        db.BOMLines.Add(new BOMLine { ParentPartId = assembly.Id, ChildPartId = part.Id, Quantity = 2 });
        var jobOnRevB = new Job { JobNumber = $"J1-{tag}", Title = "Bracket rev B", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id, PartRevision = "B" };
        var assemblyJob = new Job { JobNumber = $"J2-{tag}", Title = "Assembly", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = assembly.Id };
        var doneJob = new Job { JobNumber = $"J3-{tag}", Title = "Done", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id, CompletedDate = DateTimeOffset.UtcNow };
        var scrappedJob = new Job { JobNumber = $"J4-{tag}", Title = "Scrapped", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id, Disposition = JobDisposition.Scrap };
        db.Jobs.AddRange(jobOnRevB, assemblyJob, doneJob, scrappedJob);

        db.PurchaseOrders.AddRange(
            new PurchaseOrder
            {
                PONumber = $"PO1-{tag}", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted,
                Lines = [new PurchaseOrderLine { PartId = part.Id, Description = "Bracket", OrderedQuantity = 50, ReceivedQuantity = 20, UnitPrice = 3 }],
            },
            new PurchaseOrder
            {
                PONumber = $"PO2-{tag}", VendorId = vendor.Id, Status = PurchaseOrderStatus.Received,
                Lines = [new PurchaseOrderLine { PartId = part.Id, Description = "Bracket", OrderedQuantity = 10, ReceivedQuantity = 10, UnitPrice = 3 }],
            });
        db.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = $"SO-{tag}", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            Lines =
            [
                new SalesOrderLine { PartId = part.Id, Description = "Bracket", Quantity = 12, ShippedQuantity = 2, UnitPrice = 9, LineNumber = 1 },
                new SalesOrderLine { PartId = part.Id, Description = "Bracket", Quantity = 5, ShippedQuantity = 5, UnitPrice = 9, LineNumber = 2 },
            ],
        });
        db.LotRecords.AddRange(
            new LotRecord { LotNumber = $"LB-{tag}", PartId = part.Id, Quantity = 8, PartRevision = "B" },
            new LotRecord { LotNumber = $"LC-{tag}", PartId = part.Id, Quantity = 4, PartRevision = "C" });
        db.BinContents.AddRange(
            new BinContent { LocationId = location.Id, EntityType = "part", EntityId = part.Id, Quantity = 8, LotNumber = $"LB-{tag}", PlacedAt = DateTimeOffset.UtcNow },
            new BinContent { LocationId = location.Id, EntityType = "part", EntityId = part.Id, Quantity = 4, LotNumber = $"LC-{tag}", PlacedAt = DateTimeOffset.UtcNow },
            new BinContent { LocationId = location.Id, EntityType = "part", EntityId = part.Id, Quantity = 3, PlacedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var eco = new EngineeringChangeOrder { EcoNumber = $"ECO-{tag}", Title = "Thicker wall", Description = "x", RequestedById = 1 };
        eco.AffectedItems.Add(new EcoAffectedItem { EntityType = "Part", EntityId = part.Id, ChangeDescription = "Rev C" });
        eco.AffectedItems.Add(new EcoAffectedItem { EntityType = "BOM", EntityId = part.Id, ChangeDescription = "New fastener" });
        db.EngineeringChangeOrders.Add(eco);
        await db.SaveChangesAsync();

        var result = await new GetEcoImpactHandler(db).Handle(new GetEcoImpactQuery(eco.Id), CancellationToken.None);

        var impact = result.Parts.Should().ContainSingle().Subject;
        impact.CurrentRevision.Should().Be("C");
        impact.OpenJobs.Select(j => (j.JobNumber, j.Relation)).Should().BeEquivalentTo(
            [(jobOnRevB.JobNumber, "Direct"), (assemblyJob.JobNumber, "WhereUsed")]);
        impact.OpenJobs.Single(j => j.JobNumber == jobOnRevB.JobNumber).PartRevision.Should().Be("B");
        impact.OpenPurchaseOrderLines.Should().ContainSingle().Which.OpenQuantity.Should().Be(30);
        impact.OpenSalesOrderLines.Should().ContainSingle().Which.OpenQuantity.Should().Be(10);
        impact.StockByRevision.Select(s => (s.Revision, s.Quantity)).Should().Equal(("B", 8m), ("C", 4m), (null, 3m));
    }

    [Fact]
    public async Task An_unknown_eco_is_not_found()
    {
        await using var db = fixture.CreateContext();

        var act = () => new GetEcoImpactHandler(db).Handle(new GetEcoImpactQuery(int.MaxValue), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
