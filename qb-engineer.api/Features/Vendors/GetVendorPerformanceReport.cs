using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Vendors;

public record GetVendorPerformanceReportQuery(DateOnly? DateFrom, DateOnly? DateTo)
    : IRequest<List<VendorComparisonRowModel>>;

public class GetVendorPerformanceReportHandler(
    AppDbContext db,
    IVendorScorecardService scorecardService,
    IClock clock)
    : IRequestHandler<GetVendorPerformanceReportQuery, List<VendorComparisonRowModel>>
{
    public async Task<List<VendorComparisonRowModel>> Handle(
        GetVendorPerformanceReportQuery request, CancellationToken cancellationToken)
    {
        var dateTo = request.DateTo ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var dateFrom = request.DateFrom ?? dateTo.AddMonths(-12);

        var vendors = await db.Vendors
            .AsNoTracking()
            .Where(v => v.IsActive)
            .Select(v => new { v.Id, v.CompanyName })
            .ToListAsync(cancellationToken);

        var results = new List<VendorComparisonRowModel>();

        foreach (var vendor in vendors)
        {
            var scorecard = await scorecardService.CalculateScorecardAsync(
                vendor.Id, dateFrom, dateTo, cancellationToken);

            // Calculate trend by comparing with previous period of same length
            var periodLength = dateTo.DayNumber - dateFrom.DayNumber;
            var prevEnd = dateFrom.AddDays(-1);
            var prevStart = prevEnd.AddDays(-periodLength);

            var previousScorecard = await db.VendorScorecards
                .AsNoTracking()
                .Where(s => s.VendorId == vendor.Id
                    && s.PeriodStart == prevStart
                    && s.PeriodEnd == prevEnd)
                .FirstOrDefaultAsync(cancellationToken);

            var trend = previousScorecard is not null
                ? scorecard.OverallScore - previousScorecard.OverallScore
                : 0m;

            results.Add(new VendorComparisonRowModel(
                vendor.Id,
                vendor.CompanyName,
                scorecard.OnTimeDeliveryPercent,
                scorecard.QualityAcceptancePercent,
                scorecard.TotalSpend,
                scorecard.OverallScore,
                scorecard.Grade,
                Math.Round(trend, 1)));
        }

        return results.OrderByDescending(r => r.OverallScore).ToList();
    }
}
