using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetFmeaRiskSummaryQuery(int FmeaId) : IRequest<FmeaRiskSummaryResponseModel>;

public class GetFmeaRiskSummaryHandler(AppDbContext db)
    : IRequestHandler<GetFmeaRiskSummaryQuery, FmeaRiskSummaryResponseModel>
{
    private const int HighRpnThreshold = 200;

    public async Task<FmeaRiskSummaryResponseModel> Handle(
        GetFmeaRiskSummaryQuery request, CancellationToken cancellationToken)
    {
        var items = await db.Set<FmeaItem>()
            .AsNoTracking()
            .Where(i => i.FmeaId == request.FmeaId)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return new FmeaRiskSummaryResponseModel
            {
                TotalItems = 0,
                HighRpnItems = 0,
                AverageRpn = 0,
                MaxRpn = 0,
                RpnDistribution = [],
                HeatmapData = [],
            };
        }

        var rpns = items.Select(i => i.Severity * i.Occurrence * i.Detection).ToList();

        var distribution = new List<RpnDistributionBucket>
        {
            new() { Range = "1-50", Count = rpns.Count(r => r >= 1 && r <= 50) },
            new() { Range = "51-100", Count = rpns.Count(r => r >= 51 && r <= 100) },
            new() { Range = "101-200", Count = rpns.Count(r => r >= 101 && r <= 200) },
            new() { Range = "201-500", Count = rpns.Count(r => r >= 201 && r <= 500) },
            new() { Range = "501-1000", Count = rpns.Count(r => r >= 501 && r <= 1000) },
        };

        var heatmap = items
            .GroupBy(i => new { i.Severity, i.Occurrence, i.Detection })
            .Select(g => new RpnHeatmapCell
            {
                Severity = g.Key.Severity,
                Occurrence = g.Key.Occurrence,
                Detection = g.Key.Detection,
                Count = g.Count(),
            })
            .ToList();

        return new FmeaRiskSummaryResponseModel
        {
            TotalItems = items.Count,
            HighRpnItems = rpns.Count(r => r > HighRpnThreshold),
            AverageRpn = Math.Round((decimal)rpns.Average(), 1),
            MaxRpn = rpns.Max(),
            RpnDistribution = distribution,
            HeatmapData = heatmap,
        };
    }
}
