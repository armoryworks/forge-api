using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetSubcontractSpendingQuery(DateOnly? DateFrom, DateOnly? DateTo) : IRequest<List<SubcontractSpendingRow>>;

public class GetSubcontractSpendingHandler(AppDbContext db) : IRequestHandler<GetSubcontractSpendingQuery, List<SubcontractSpendingRow>>
{
    public async Task<List<SubcontractSpendingRow>> Handle(GetSubcontractSpendingQuery request, CancellationToken ct)
    {
        var query = db.SubcontractOrders
            .AsNoTracking()
            .Include(o => o.Vendor)
            .Include(o => o.Operation)
            .AsQueryable();

        if (request.DateFrom.HasValue)
        {
            var from = new DateTimeOffset(request.DateFrom.Value, TimeOnly.MinValue, TimeSpan.Zero);
            query = query.Where(o => o.SentAt >= from);
        }

        if (request.DateTo.HasValue)
        {
            var to = new DateTimeOffset(request.DateTo.Value, TimeOnly.MaxValue, TimeSpan.Zero);
            query = query.Where(o => o.SentAt <= to);
        }

        var orders = await query.ToListAsync(ct);

        var grouped = orders
            .GroupBy(o => new { o.VendorId, VendorName = o.Vendor.CompanyName })
            .Select(g =>
            {
                var completed = g.Where(o => o.Status == SubcontractStatus.Complete).ToList();
                var received = g.Where(o => o.ReceivedAt.HasValue).ToList();
                var onTime = received.Count > 0
                    ? received.Count(o => o.ExpectedReturnDate.HasValue && o.ReceivedAt <= o.ExpectedReturnDate) / (decimal)received.Count * 100
                    : 0;
                var accepted = received.Count > 0
                    ? received.Count(o => o.Status == SubcontractStatus.Complete) / (decimal)received.Count * 100
                    : 0;
                var avgLead = received.Count > 0
                    ? received.Average(o => (o.ReceivedAt!.Value - o.SentAt).TotalDays)
                    : 0;

                return new SubcontractSpendingRow
                {
                    VendorId = g.Key.VendorId,
                    VendorName = g.Key.VendorName,
                    OperationType = string.Join(", ", g.Select(o => o.Operation.Title).Distinct().Take(3)),
                    OrderCount = g.Count(),
                    TotalSpend = g.Sum(o => o.Quantity * o.UnitCost),
                    AvgLeadTimeDays = (decimal)avgLead,
                    OnTimePercent = onTime,
                    QualityAcceptPercent = accepted,
                };
            })
            .OrderByDescending(r => r.TotalSpend)
            .ToList();

        return grouped;
    }
}
