using MediatR;

using Forge.Api.Features.Reports;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Dashboard;

public record GetMarginSummaryQuery : IRequest<MarginSummaryResponseModel>;

public class GetMarginSummaryHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetMarginSummaryQuery, MarginSummaryResponseModel>
{
    public async Task<MarginSummaryResponseModel> Handle(GetMarginSummaryQuery request, CancellationToken ct)
    {
        var rows = await JobMarginCalculator.LoadAsync(db, clock.UtcNow.AddDays(-30), null, ct);

        if (rows.Count == 0)
            return new MarginSummaryResponseModel(0, 0, 0, 0, 0, 0);

        var totalRevenue = rows.Sum(r => r.Revenue);
        var totalCost = rows.Sum(r => r.TotalCost);

        return new MarginSummaryResponseModel(
            totalRevenue,
            totalCost,
            totalRevenue - totalCost,
            JobMarginCalculator.WeightedMarginPercentage(rows),
            rows.Count,
            rows.Count(r => r.TotalCost > 0));
    }
}
