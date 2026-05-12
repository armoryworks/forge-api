using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GetUninvoicedJobsQuery : IRequest<List<UninvoicedJobResponseModel>>;

public class GetUninvoicedJobsHandler(AppDbContext db)
    : IRequestHandler<GetUninvoicedJobsQuery, List<UninvoicedJobResponseModel>>
{
    public async Task<List<UninvoicedJobResponseModel>> Handle(
        GetUninvoicedJobsQuery request, CancellationToken cancellationToken)
    {
        // Find completed, non-archived jobs that have no invoice linked.
        // A job is "invoiced" if:
        //   1. It has a SalesOrderLine → SalesOrder → Invoice chain, OR
        //   2. (Direct mode) We can also check if an invoice exists referencing the same SalesOrder.
        // Simplest approach: left-join through SalesOrderLine → SalesOrder → Invoices.
        // Jobs without a SalesOrderLine are uninvoiced by definition if completed.
        // Jobs with a SalesOrderLine are uninvoiced if the SalesOrder has no invoices.

        var uninvoicedJobs = await db.Jobs
            .Include(j => j.Customer)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
            .Where(j => j.CompletedDate != null && !j.IsArchived)
            .Where(j =>
                // No sales order line at all (direct job, never linked)
                j.SalesOrderLineId == null ||
                // Has sales order line but the sales order has no invoices
                !j.SalesOrderLine!.SalesOrder.Invoices.Any(i => i.DeletedAt == null))
            .OrderBy(j => j.CompletedDate)
            .Select(j => new UninvoicedJobResponseModel(
                j.Id,
                j.JobNumber,
                j.Title,
                j.Customer != null ? j.Customer.Name : null,
                j.CompletedDate!.Value,
                j.CustomerId))
            .ToListAsync(cancellationToken);

        return uninvoicedJobs;
    }
}
