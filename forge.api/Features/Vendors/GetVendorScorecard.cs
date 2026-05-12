using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

public record GetVendorScorecardQuery(int VendorId, DateOnly? DateFrom, DateOnly? DateTo)
    : IRequest<VendorScorecardResponseModel>;

public class GetVendorScorecardHandler(IVendorScorecardService scorecardService, IClock clock)
    : IRequestHandler<GetVendorScorecardQuery, VendorScorecardResponseModel>
{
    public async Task<VendorScorecardResponseModel> Handle(
        GetVendorScorecardQuery request, CancellationToken cancellationToken)
    {
        var dateTo = request.DateTo ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var dateFrom = request.DateFrom ?? dateTo.AddMonths(-12);

        var scorecard = await scorecardService.CalculateScorecardAsync(
            request.VendorId, dateFrom, dateTo, cancellationToken);

        return new VendorScorecardResponseModel(
            scorecard.Id,
            scorecard.VendorId,
            scorecard.Vendor.CompanyName,
            scorecard.PeriodStart,
            scorecard.PeriodEnd,
            scorecard.TotalPurchaseOrders,
            scorecard.TotalLinesReceived,
            scorecard.OnTimeDeliveries,
            scorecard.LateDeliveries,
            scorecard.EarlyDeliveries,
            scorecard.AvgLeadTimeDays,
            scorecard.OnTimeDeliveryPercent,
            scorecard.TotalInspected,
            scorecard.TotalAccepted,
            scorecard.TotalRejected,
            scorecard.TotalNcrs,
            scorecard.QualityAcceptancePercent,
            scorecard.TotalSpend,
            scorecard.AvgPriceVariancePercent,
            scorecard.CostIncreaseCount,
            scorecard.QuantityShortages,
            scorecard.QuantityOverages,
            scorecard.QuantityAccuracyPercent,
            scorecard.OverallScore,
            scorecard.Grade,
            scorecard.CalculatedAt,
            scorecard.CalculationNotes);
    }
}
