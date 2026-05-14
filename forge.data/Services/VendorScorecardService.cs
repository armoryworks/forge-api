using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class VendorScorecardService(
    AppDbContext db,
    IClock clock,
    ILogger<VendorScorecardService> logger) : IVendorScorecardService
{
    private const decimal DeliveryWeight = 0.40m;
    private const decimal QualityWeight = 0.30m;
    private const decimal PriceWeight = 0.20m;
    private const decimal QuantityWeight = 0.10m;

    public async Task<VendorScorecard> CalculateScorecardAsync(
        int vendorId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        var periodStartDto = new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEndDto = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        // --- Delivery metrics ---
        var purchaseOrders = await db.PurchaseOrders
            .AsNoTracking()
            .Where(po => po.VendorId == vendorId
                && po.CreatedAt >= periodStartDto
                && po.CreatedAt <= periodEndDto)
            .Select(po => new
            {
                po.Id,
                po.ExpectedDeliveryDate,
                po.ReceivedDate,
                po.Status,
                LineCount = po.Lines.Count,
            })
            .ToListAsync(ct);

        var totalPurchaseOrders = purchaseOrders.Count;

        var receivedPos = purchaseOrders
            .Where(po => po.ReceivedDate.HasValue && po.ExpectedDeliveryDate.HasValue)
            .ToList();

        var onTimeDeliveries = receivedPos.Count(po => po.ReceivedDate!.Value <= po.ExpectedDeliveryDate!.Value);
        var lateDeliveries = receivedPos.Count(po => po.ReceivedDate!.Value > po.ExpectedDeliveryDate!.Value);
        var earlyDeliveries = receivedPos.Count(po =>
            po.ReceivedDate!.Value < po.ExpectedDeliveryDate!.Value.AddDays(-1));

        var avgLeadTimeDays = receivedPos.Count > 0
            ? (decimal)receivedPos
                .Where(po => po.ReceivedDate.HasValue)
                .Average(po => (po.ReceivedDate!.Value - periodStartDto).TotalDays)
            : 0m;

        var totalDeliveries = onTimeDeliveries + lateDeliveries;
        var onTimeDeliveryPercent = totalDeliveries > 0
            ? Math.Round((decimal)onTimeDeliveries / totalDeliveries * 100, 2)
            : 100m;

        // --- Lines received ---
        var poIds = purchaseOrders.Select(po => po.Id).ToList();

        var totalLinesReceived = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(l => poIds.Contains(l.PurchaseOrderId) && l.ReceivedQuantity > 0)
            .CountAsync(ct);

        // --- Quality metrics (from receiving inspections) ---
        var receivingRecords = await db.ReceivingRecords
            .AsNoTracking()
            .Where(rr => poIds.Contains(rr.PurchaseOrderLine.PurchaseOrderId)
                && rr.InspectionStatus != ReceivingInspectionStatus.NotRequired)
            .Select(rr => new
            {
                rr.InspectionStatus,
                Accepted = rr.InspectedQuantityAccepted ?? 0,
                Rejected = rr.InspectedQuantityRejected ?? 0,
            })
            .ToListAsync(ct);

        var totalInspected = receivingRecords.Count;
        var totalAccepted = receivingRecords.Count(rr =>
            rr.InspectionStatus == ReceivingInspectionStatus.Passed);
        var totalRejected = receivingRecords.Count(rr =>
            rr.InspectionStatus == ReceivingInspectionStatus.Failed);

        var qualityAcceptancePercent = totalInspected > 0
            ? Math.Round((decimal)totalAccepted / totalInspected * 100, 2)
            : 100m;

        // --- NCR count for this vendor in period ---
        var totalNcrs = await db.NonConformances
            .AsNoTracking()
            .Where(ncr => ncr.VendorId == vendorId
                && ncr.DetectedAt >= periodStartDto
                && ncr.DetectedAt <= periodEndDto)
            .CountAsync(ct);

        // --- Price metrics ---
        var lineData = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(l => poIds.Contains(l.PurchaseOrderId))
            .Select(l => new
            {
                l.OrderedQuantity,
                l.ReceivedQuantity,
                l.UnitPrice,
            })
            .ToListAsync(ct);

        var totalSpend = lineData.Sum(l => l.UnitPrice * l.ReceivedQuantity);

        // Price variance: simplified — no historical baseline in current schema,
        // so we set 0% variance (no change detected). A future enhancement could
        // compare against PriceListEntry or previous period PO prices.
        var avgPriceVariancePercent = 0m;
        var costIncreaseCount = 0;

        // --- Quantity metrics ---
        var quantityShortages = lineData.Count(l => l.ReceivedQuantity < l.OrderedQuantity && l.ReceivedQuantity > 0);
        var quantityOverages = lineData.Count(l => l.ReceivedQuantity > l.OrderedQuantity);
        var totalLinesWithReceipts = lineData.Count(l => l.ReceivedQuantity > 0);
        var accurateLines = totalLinesWithReceipts - quantityShortages - quantityOverages;
        var quantityAccuracyPercent = totalLinesWithReceipts > 0
            ? Math.Round((decimal)Math.Max(accurateLines, 0) / totalLinesWithReceipts * 100, 2)
            : 100m;

        // --- Overall score ---
        // Price score: 100 if no variance, decreasing with variance
        var priceScore = Math.Max(0, 100m - Math.Abs(avgPriceVariancePercent) * 5);

        var overallScore = CalculateOverallScore(
            onTimeDeliveryPercent, qualityAcceptancePercent, priceScore, quantityAccuracyPercent);
        var grade = DetermineGrade(overallScore);

        var scorecard = new VendorScorecard
        {
            VendorId = vendorId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalPurchaseOrders = totalPurchaseOrders,
            TotalLinesReceived = totalLinesReceived,
            OnTimeDeliveries = onTimeDeliveries,
            LateDeliveries = lateDeliveries,
            EarlyDeliveries = earlyDeliveries,
            AvgLeadTimeDays = Math.Round(avgLeadTimeDays, 1),
            OnTimeDeliveryPercent = onTimeDeliveryPercent,
            TotalInspected = totalInspected,
            TotalAccepted = totalAccepted,
            TotalRejected = totalRejected,
            TotalNcrs = totalNcrs,
            QualityAcceptancePercent = qualityAcceptancePercent,
            TotalSpend = Math.Round(totalSpend, 2),
            AvgPriceVariancePercent = avgPriceVariancePercent,
            CostIncreaseCount = costIncreaseCount,
            QuantityShortages = quantityShortages,
            QuantityOverages = quantityOverages,
            QuantityAccuracyPercent = quantityAccuracyPercent,
            OverallScore = overallScore,
            Grade = grade,
            CalculatedAt = clock.UtcNow,
        };

        // Upsert: replace existing scorecard for the same vendor + period
        var existing = await db.VendorScorecards
            .FirstOrDefaultAsync(s => s.VendorId == vendorId
                && s.PeriodStart == periodStart
                && s.PeriodEnd == periodEnd, ct);

        if (existing is not null)
        {
            existing.TotalPurchaseOrders = scorecard.TotalPurchaseOrders;
            existing.TotalLinesReceived = scorecard.TotalLinesReceived;
            existing.OnTimeDeliveries = scorecard.OnTimeDeliveries;
            existing.LateDeliveries = scorecard.LateDeliveries;
            existing.EarlyDeliveries = scorecard.EarlyDeliveries;
            existing.AvgLeadTimeDays = scorecard.AvgLeadTimeDays;
            existing.OnTimeDeliveryPercent = scorecard.OnTimeDeliveryPercent;
            existing.TotalInspected = scorecard.TotalInspected;
            existing.TotalAccepted = scorecard.TotalAccepted;
            existing.TotalRejected = scorecard.TotalRejected;
            existing.TotalNcrs = scorecard.TotalNcrs;
            existing.QualityAcceptancePercent = scorecard.QualityAcceptancePercent;
            existing.TotalSpend = scorecard.TotalSpend;
            existing.AvgPriceVariancePercent = scorecard.AvgPriceVariancePercent;
            existing.CostIncreaseCount = scorecard.CostIncreaseCount;
            existing.QuantityShortages = scorecard.QuantityShortages;
            existing.QuantityOverages = scorecard.QuantityOverages;
            existing.QuantityAccuracyPercent = scorecard.QuantityAccuracyPercent;
            existing.OverallScore = scorecard.OverallScore;
            existing.Grade = scorecard.Grade;
            existing.CalculatedAt = scorecard.CalculatedAt;
            scorecard = existing;
        }
        else
        {
            db.VendorScorecards.Add(scorecard);
        }

        await db.SaveChangesAsync(ct);

        // Ensure Vendor navigation is loaded for response mapping
        if (scorecard.Vendor is null)
        {
            await db.Entry(scorecard).Reference(s => s.Vendor).LoadAsync(ct);
        }

        logger.LogInformation(
            "Calculated scorecard for Vendor {VendorId}: Score={Score}, Grade={Grade}",
            vendorId, overallScore, grade);

        return scorecard;
    }

    public async Task RecalculateAllAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        var vendorIds = await db.Vendors
            .AsNoTracking()
            .Where(v => v.IsActive)
            .Select(v => v.Id)
            .ToListAsync(ct);

        logger.LogInformation(
            "Recalculating scorecards for {Count} vendors, period {Start} to {End}",
            vendorIds.Count, periodStart, periodEnd);

        foreach (var vendorId in vendorIds)
        {
            try
            {
                await CalculateScorecardAsync(vendorId, periodStart, periodEnd, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to calculate scorecard for Vendor {VendorId}", vendorId);
            }
        }
    }

    public decimal CalculateOverallScore(
        decimal onTimePercent, decimal qualityPercent, decimal priceScore, decimal quantityAccuracyPercent)
    {
        var score = (onTimePercent * DeliveryWeight)
            + (qualityPercent * QualityWeight)
            + (priceScore * PriceWeight)
            + (quantityAccuracyPercent * QuantityWeight);

        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }

    public VendorGrade DetermineGrade(decimal overallScore) => overallScore switch
    {
        >= 90 => VendorGrade.A,
        >= 80 => VendorGrade.B,
        >= 70 => VendorGrade.C,
        >= 60 => VendorGrade.D,
        _ => VendorGrade.F,
    };
}
