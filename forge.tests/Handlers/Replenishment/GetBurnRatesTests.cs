using FluentAssertions;

using Forge.Api.Features.Replenishment;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Replenishment;

public class GetBurnRatesTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private GetBurnRatesHandler Handler() => new(_db, new PartSourcingResolver(_db), new FixedClock());

    private async Task<Part> SeedPartAsync(string partNumber, ProcurementSource source)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = source,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            SafetyStockDays = 2,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        _db.BinContents.Add(new BinContent { EntityType = "part", EntityId = part.Id, Quantity = 10m, LocationId = 1 });
        for (var d = 1; d <= 90; d++)
            _db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = part.Id,
                Quantity = 5m,
                Reason = BinMovementReason.Ship,
                MovedAt = FixedNow.AddDays(-d),
            });
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task SeedOpenJobAsync(int partId, decimal quantity, DateTimeOffset dueDate)
    {
        var job = new Job
        {
            JobNumber = $"J-{partId}",
            Title = "Build",
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = partId,
            DueDate = dueDate,
        };
        job.JobParts.Add(new JobPart { PartId = partId, Quantity = quantity });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
    }

    private async Task<Vendor> SeedOpenPoAsync(int partId, decimal quantity, DateTimeOffset expected)
    {
        var vendor = new Vendor { CompanyName = $"Vendor {partId}" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = $"PO-{partId}",
            VendorId = vendor.Id,
            Status = PurchaseOrderStatus.Submitted,
            ExpectedDeliveryDate = expected,
        };
        po.Lines.Add(new PurchaseOrderLine { PartId = partId, Description = "Line", OrderedQuantity = quantity });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return vendor;
    }

    [Fact]
    public async Task Make_part_uses_open_job_supply_and_the_routing_lead_time()
    {
        var part = await SeedPartAsync("MAKE-BURN", ProcurementSource.Make);
        _db.Operations.Add(new Operation { PartId = part.Id, StepNumber = 10, Title = "Mold", RunMinutesEach = 48m });
        await SeedOpenJobAsync(part.Id, 10m, FixedNow.AddDays(3));
        await SeedOpenPoAsync(part.Id, 500m, FixedNow.AddDays(1));

        var row = (await Handler().Handle(new GetBurnRatesQuery(null, false), default)).Single();

        row.IncomingPoQuantity.Should().Be(10m);
        row.EarliestPoArrival.Should().Be(FixedNow.AddDays(3));
        row.LeadTimeDays.Should().Be(4);
        row.NeedsReorder.Should().BeTrue();
    }

    [Fact]
    public async Task Buy_part_uses_open_purchase_orders_and_the_vendor_lead_time()
    {
        var part = await SeedPartAsync("BUY-BURN", ProcurementSource.Buy);
        var vendor = await SeedOpenPoAsync(part.Id, 50m, FixedNow.AddDays(5));
        _db.VendorParts.Add(new VendorPart { VendorId = vendor.Id, PartId = part.Id, IsPreferred = true, LeadTimeDays = 21 });
        await _db.SaveChangesAsync();
        await SeedOpenJobAsync(part.Id, 999m, FixedNow.AddDays(2));

        var row = (await Handler().Handle(new GetBurnRatesQuery(null, false), default)).Single();

        row.IncomingPoQuantity.Should().Be(50m);
        row.EarliestPoArrival.Should().Be(FixedNow.AddDays(5));
        row.LeadTimeDays.Should().Be(21);
        row.NeedsReorder.Should().BeTrue();
    }
}
