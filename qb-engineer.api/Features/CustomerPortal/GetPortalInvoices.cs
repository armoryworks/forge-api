using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — portal invoice list. ⚡ Standalone Financial: returns
/// an empty list when an external accounting provider is connected, so
/// the customer doesn't see partial / divergent invoice data — the
/// integrated provider's portal is the source of truth in that mode.
/// </summary>
public record GetPortalInvoicesQuery(int CustomerId) : IRequest<List<PortalInvoiceListItem>>;

public class GetPortalInvoicesHandler(AppDbContext db, IAccountingService accounting)
    : IRequestHandler<GetPortalInvoicesQuery, List<PortalInvoiceListItem>>
{
    public async Task<List<PortalInvoiceListItem>> Handle(GetPortalInvoicesQuery request, CancellationToken ct)
    {
        var standalone = string.Equals(accounting.ProviderId, "mock", StringComparison.OrdinalIgnoreCase);
        if (!standalone) return new List<PortalInvoiceListItem>();

        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => i.CustomerId == request.CustomerId)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(200)
            .ToListAsync(ct);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var paidByInvoice = await db.PaymentApplications.AsNoTracking()
            .Where(pa => invoiceIds.Contains(pa.InvoiceId))
            .GroupBy(pa => pa.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        return invoices.Select(i =>
        {
            var paid = paidByInvoice.GetValueOrDefault(i.Id, 0m);
            return new PortalInvoiceListItem(
                Id: i.Id,
                InvoiceNumber: i.InvoiceNumber,
                Status: i.Status.ToString(),
                InvoiceDate: i.InvoiceDate,
                DueDate: i.DueDate,
                Total: i.Total,
                AmountPaid: paid,
                Balance: i.Total - paid);
        }).ToList();
    }
}
