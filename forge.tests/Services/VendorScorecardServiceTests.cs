using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class VendorScorecardServiceTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly PeriodStart = new(2026, 9, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 9, 30);

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private VendorScorecardService Service()
        => new(_db, new FixedClock(), NullLogger<VendorScorecardService>.Instance);

    private async Task<int> SeedVendorWithReceiptsAsync(
        params (ReceivingInspectionStatus Status, decimal? Accepted, decimal? Rejected)[] receipts)
    {
        var vendor = new Vendor { CompanyName = "Supplier" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            PONumber = "PO-2001",
            VendorId = vendor.Id,
            Status = PurchaseOrderStatus.Received,
            CreatedAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            Description = "Bracket",
            OrderedQuantity = 10m * receipts.Length,
            ReceivedQuantity = 10m * receipts.Length,
            UnitPrice = 5m,
        };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();

        var n = 0;
        foreach (var (status, accepted, rejected) in receipts)
        {
            _db.ReceivingRecords.Add(new ReceivingRecord
            {
                PurchaseOrderLineId = line.Id,
                QuantityReceived = 10m,
                ReceiptNumber = $"R-20260910-{++n:0000}",
                InspectionStatus = status,
                InspectedQuantityAccepted = accepted,
                InspectedQuantityRejected = rejected,
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return vendor.Id;
    }

    [Fact]
    public async Task Quality_IgnoresReceiptsStillInTheQueueAndWaived()
    {
        var vendorId = await SeedVendorWithReceiptsAsync(
            (ReceivingInspectionStatus.Pending, null, null),
            (ReceivingInspectionStatus.Pending, null, null),
            (ReceivingInspectionStatus.InProgress, null, null),
            (ReceivingInspectionStatus.Waived, null, null),
            (ReceivingInspectionStatus.NotRequired, null, null),
            (ReceivingInspectionStatus.Passed, 10m, 0m));

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.TotalInspected.Should().Be(1);
        scorecard.TotalAccepted.Should().Be(1);
        scorecard.TotalRejected.Should().Be(0);
        scorecard.QualityAcceptancePercent.Should().Be(100m);
    }

    [Fact]
    public async Task Quality_CountsPartialAcceptByAcceptedShare()
    {
        var vendorId = await SeedVendorWithReceiptsAsync(
            (ReceivingInspectionStatus.Passed, 10m, 0m),
            (ReceivingInspectionStatus.PartialAccept, 8m, 2m),
            (ReceivingInspectionStatus.Failed, 0m, 10m),
            (ReceivingInspectionStatus.Pending, null, null));

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.TotalInspected.Should().Be(3);
        scorecard.TotalAccepted.Should().Be(1);
        scorecard.TotalRejected.Should().Be(1);
        scorecard.QualityAcceptancePercent.Should().Be(60m);
    }

    [Fact]
    public async Task Quality_WithOnlyQueuedReceipts_StaysAtFullScore()
    {
        var vendorId = await SeedVendorWithReceiptsAsync(
            (ReceivingInspectionStatus.Pending, null, null),
            (ReceivingInspectionStatus.InProgress, null, null));

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.TotalInspected.Should().Be(0);
        scorecard.QualityAcceptancePercent.Should().Be(100m);
    }
}
