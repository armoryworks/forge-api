using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetLeadSalesReportQuery(DateTimeOffset Start, DateTimeOffset End)
    : IRequest<LeadSalesReportItem>;

public class GetLeadSalesReportHandler(AppDbContext db)
    : IRequestHandler<GetLeadSalesReportQuery, LeadSalesReportItem>
{
    public async Task<LeadSalesReportItem> Handle(GetLeadSalesReportQuery request, CancellationToken ct)
    {
        var startUtc = request.Start.UtcDateTime;
        var endUtc = request.End.UtcDateTime;

        var newLeads = await db.Leads
            .CountAsync(l => l.CreatedAt >= startUtc && l.CreatedAt <= endUtc, ct);

        var convertedLeads = await db.Leads
            .CountAsync(l => l.CreatedAt >= startUtc && l.CreatedAt <= endUtc
                && l.Status == LeadStatus.Converted, ct);

        var conversionRate = newLeads > 0
            ? Math.Round((decimal)convertedLeads / newLeads * 100, 1)
            : 0;

        var totalQuotes = await db.Quotes
            .CountAsync(q => q.CreatedAt >= startUtc && q.CreatedAt <= endUtc, ct);

        var salesOrders = await db.SalesOrders
            .Include(so => so.Lines)
            .Where(so => so.CreatedAt >= startUtc && so.CreatedAt <= endUtc)
            .ToListAsync(ct);

        var totalSalesOrders = salesOrders.Count;
        var totalSoValue = salesOrders.Sum(so => so.Lines.Sum(l => l.LineTotal));

        return new LeadSalesReportItem(
            newLeads, convertedLeads, conversionRate,
            totalQuotes, totalSalesOrders, totalSoValue,
            startUtc, endUtc);
    }
}
