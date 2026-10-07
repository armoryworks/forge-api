using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

/// <summary>
/// #27 — sales-order lines that can be associated with a new job. By default returns
/// only lines NOT actively assigned to an open job (no open job links); set
/// <paramref name="IncludeAssigned"/> to also surface already-assigned lines. The
/// optional <paramref name="Search"/> matches order number, line description, or part number.
/// </summary>
public record GetAssignableSalesOrderLinesQuery(bool IncludeAssigned, string? Search)
    : IRequest<List<AssignableSalesOrderLineModel>>;

public class GetAssignableSalesOrderLinesHandler(AppDbContext db, ISalesOrderAcceptanceGate acceptanceGate)
    : IRequestHandler<GetAssignableSalesOrderLinesQuery, List<AssignableSalesOrderLineModel>>
{
    public async Task<List<AssignableSalesOrderLineModel>> Handle(
        GetAssignableSalesOrderLinesQuery request, CancellationToken cancellationToken)
    {
        var query = db.SalesOrderLines
            .AsNoTracking()
            .Where(l =>
                l.SalesOrder.Status == SalesOrderStatus.Confirmed ||
                l.SalesOrder.Status == SalesOrderStatus.InProduction ||
                l.SalesOrder.Status == SalesOrderStatus.PartiallyShipped);

        // When the acceptance gate is on, only offer lines whose SO has accepted proof — otherwise a
        // job linked from the board would be rejected by CreateJobHandler anyway.
        if (acceptanceGate.IsEnabled)
            query = query.Where(l => db.Attestations
                .Any(a => a.SalesOrderId == l.SalesOrderId && a.Status == AcceptanceStatus.Accepted));

        // "Actively assigned" = has at least one open job (not archived, not disposed).
        // The soft-delete global filter already excludes deleted jobs from the nav.
        if (!request.IncludeAssigned)
            query = query.Where(l => !l.Jobs.Any(j => !j.IsArchived && j.Disposition == null));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(l =>
                l.SalesOrder.OrderNumber.Contains(term) ||
                l.Description.Contains(term) ||
                (l.Part != null && l.Part.PartNumber.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(l => l.SalesOrderId)
            .ThenBy(l => l.LineNumber)
            .Select(l => new
            {
                l.Id,
                l.SalesOrderId,
                l.SalesOrder.OrderNumber,
                l.LineNumber,
                l.PartId,
                PartNumber = l.Part != null ? l.Part.PartNumber : null,
                l.Description,
                l.Quantity,
                l.ShippedQuantity,
                AssignedJobCount = l.Jobs.Count(j => !j.IsArchived && j.Disposition == null),
                OnJobsInProgress = l.Jobs
                    .Where(j => !j.IsArchived && j.Disposition == null && j.CompletedDate == null)
                    .SelectMany(j => j.JobParts.Where(jp => jp.PartId == j.PartId))
                    .Sum(jp => (decimal?)jp.Quantity) ?? 0m,
                OnCompletedJobs = l.Jobs
                    .Where(j => !j.IsArchived && j.Disposition == null && j.CompletedDate != null)
                    .SelectMany(j => j.JobParts.Where(jp => jp.PartId == j.PartId))
                    .Sum(jp => (decimal?)jp.Quantity) ?? 0m,
                l.SalesOrder.RequestedDeliveryDate,
            })
            .Take(100)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AssignableSalesOrderLineModel(
                r.Id,
                r.SalesOrderId,
                r.OrderNumber,
                r.LineNumber,
                r.PartId,
                r.PartNumber,
                r.Description,
                r.Quantity,
                r.AssignedJobCount,
                SalesOrderLineDefaultQuantity.Remaining(r.Quantity, r.ShippedQuantity, r.OnJobsInProgress, r.OnCompletedJobs),
                r.RequestedDeliveryDate))
            .ToList();
    }
}
