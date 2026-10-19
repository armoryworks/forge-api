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

    private static DateTimeOffset Sep(int day) => new(2026, 9, day, 12, 0, 0, TimeSpan.Zero);

    private async Task<int> SeedVendorAsync()
    {
        var vendor = new Vendor { CompanyName = "Supplier" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        return vendor.Id;
    }

    private async Task<PurchaseOrder> SeedPoAsync(
        int vendorId, PurchaseOrderStatus status, DateTimeOffset? expected,
        DateTimeOffset? submitted = null, DateTimeOffset? received = null, bool isBlanket = false)
    {
        var po = new PurchaseOrder
        {
            PONumber = $"PO-{Guid.NewGuid():N}"[..12],
            VendorId = vendorId,
            Status = status,
            IsBlanket = isBlanket,
            ExpectedDeliveryDate = expected,
            SubmittedDate = submitted,
            ReceivedDate = received,
            CreatedAt = Sep(2),
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return po;
    }

    [Fact]
    public async Task LeadTime_IsMeasuredFromSubmissionToReceipt()
    {
        var vendorId = await SeedVendorAsync();
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Received, Sep(25), submitted: Sep(12), received: Sep(22));
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Received, Sep(25), submitted: Sep(20), received: Sep(20));
        _db.ChangeTracker.Clear();

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.AvgLeadTimeDays.Should().Be(5m, "10 days and 0 days average to 5, not the days since the period start");
        scorecard.OnTimeDeliveries.Should().Be(2);
    }

    [Fact]
    public async Task OpenPoPastItsPromiseDate_CountsAsLate()
    {
        var vendorId = await SeedVendorAsync();
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Received, Sep(20), submitted: Sep(5), received: Sep(18));
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Acknowledged, Sep(21), submitted: Sep(5));
        await SeedPoAsync(vendorId, PurchaseOrderStatus.PartiallyReceived, Sep(22), submitted: Sep(5));
        _db.ChangeTracker.Clear();

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.OnTimeDeliveries.Should().Be(1);
        scorecard.LateDeliveries.Should().Be(2);
        scorecard.OnTimeDeliveryPercent.Should().BeApproximately(33.33m, 0.01m);
    }

    [Fact]
    public async Task OpenPoNotYetDue_AndCancelledOrDraftPos_AreNotCounted()
    {
        var vendorId = await SeedVendorAsync();
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Submitted, new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), submitted: Sep(5));
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Cancelled, Sep(10), submitted: Sep(5));
        await SeedPoAsync(vendorId, PurchaseOrderStatus.Draft, Sep(10));
        _db.ChangeTracker.Clear();

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.LateDeliveries.Should().Be(0);
        scorecard.OnTimeDeliveries.Should().Be(0);
        scorecard.OnTimeDeliveryPercent.Should().Be(100m, "nothing was due yet");
    }

    [Fact]
    public async Task BlanketReleases_AreScoredAgainstTheirRequestedDates()
    {
        var vendorId = await SeedVendorAsync();
        var blanket = await SeedPoAsync(vendorId, PurchaseOrderStatus.PartiallyReceived, Sep(28), submitted: Sep(3), isBlanket: true);
        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = blanket.Id,
            Description = "Resin",
            OrderedQuantity = 30m,
            ReceivedQuantity = 10m,
            UnitPrice = 2m,
        };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();

        _db.PurchaseOrderReleases.AddRange(
            new PurchaseOrderRelease { PurchaseOrderId = blanket.Id, PurchaseOrderLineId = line.Id, ReleaseNumber = 1, Quantity = 10m, RequestedDeliveryDate = Sep(15) },
            new PurchaseOrderRelease { PurchaseOrderId = blanket.Id, PurchaseOrderLineId = line.Id, ReleaseNumber = 2, Quantity = 10m, RequestedDeliveryDate = Sep(23) },
            new PurchaseOrderRelease { PurchaseOrderId = blanket.Id, PurchaseOrderLineId = line.Id, ReleaseNumber = 3, Quantity = 10m, RequestedDeliveryDate = Sep(24), Status = PurchaseOrderReleaseStatus.Cancelled });
        _db.ReceivingRecords.Add(new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id,
            QuantityReceived = 10m,
            ReceiptNumber = "R-20260914-0001",
            CreatedAt = Sep(14),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var scorecard = await Service().CalculateScorecardAsync(vendorId, PeriodStart, PeriodEnd);

        scorecard.OnTimeDeliveries.Should().Be(1, "release 1 was covered by the receipt the day before it was due");
        scorecard.LateDeliveries.Should().Be(1, "release 2 is past its requested date with nothing received for it");
        scorecard.OnTimeDeliveryPercent.Should().Be(50m);
    }
}
