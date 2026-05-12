using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

/// <summary>
/// Phase 1r / Batch 16 — win/loss by part-class. Aggregates leads by
/// their PartClassCode and counts Converted / Lost / active outcomes.
/// Leads with no PartClassCode are bucketed under "(unclassified)" so
/// the data-quality gap surfaces in the report itself rather than
/// silently disappearing.
/// </summary>
public record GetWinLossByClassReportQuery(DateTimeOffset? Start, DateTimeOffset? End)
    : IRequest<List<WinLossByClassReportItem>>;

public class GetWinLossByClassReportHandler(AppDbContext db)
    : IRequestHandler<GetWinLossByClassReportQuery, List<WinLossByClassReportItem>>
{
    private const string UnclassifiedBucket = "(unclassified)";

    public async Task<List<WinLossByClassReportItem>> Handle(GetWinLossByClassReportQuery request, CancellationToken ct)
    {
        var query = db.Leads.AsNoTracking().AsQueryable();
        if (request.Start.HasValue) query = query.Where(l => l.CreatedAt >= request.Start.Value);
        if (request.End.HasValue) query = query.Where(l => l.CreatedAt <= request.End.Value);

        // GroupBy in EF doesn't translate a Coalesce + count-with-predicate
        // shape cleanly across providers, so pull the small projection
        // client-side and bucket in memory. Lead volume per install is
        // expected to be < 100k even after multiple years — fine to scan.
        var rows = await query
            .Select(l => new { l.PartClassCode, l.Status })
            .ToListAsync(ct);

        var buckets = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.PartClassCode) ? UnclassifiedBucket : r.PartClassCode!)
            .Select(g =>
            {
                var converted = g.Count(r => r.Status == LeadStatus.Converted);
                var lost = g.Count(r => r.Status == LeadStatus.Lost);
                var active = g.Count(r => r.Status != LeadStatus.Converted && r.Status != LeadStatus.Lost);
                var total = g.Count();
                var resolved = converted + lost;
                var winRate = resolved == 0 ? 0.0 : (double)converted / resolved * 100.0;
                return new WinLossByClassReportItem(g.Key, converted, lost, active, total, Math.Round(winRate, 1));
            })
            .OrderByDescending(r => r.TotalLeads)
            .ToList();

        return buckets;
    }
}
