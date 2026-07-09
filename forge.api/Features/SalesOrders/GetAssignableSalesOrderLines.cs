using MediatR;
using Microsoft.EntityFrameworkCore;
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
        // Cancelled orders aren't workable, so their lines are never assignable.
        var query = db.SalesOrderLines
            .AsNoTracking()
            .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled);

        // When the acceptance gate is on, only offer lines whose SO has accepted proof — otherwise a
        // job linked from the board would be rejected by CreateJobHandler anyway.
        if (acceptanceGate.IsEnabled)
            query = query.Where(l => db.SalesOrderAcceptances
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

        return await query
            .OrderByDescending(l => l.SalesOrderId)
            .ThenBy(l => l.LineNumber)
            .Select(l => new AssignableSalesOrderLineModel(
                l.Id,
                l.SalesOrderId,
                l.SalesOrder.OrderNumber,
                l.LineNumber,
                l.PartId,
                l.Part != null ? l.Part.PartNumber : null,
                l.Description,
                l.Quantity,
                l.Jobs.Count(j => !j.IsArchived && j.Disposition == null)))
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}
