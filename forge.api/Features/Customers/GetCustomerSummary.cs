using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers;

public record GetCustomerSummaryQuery(int Id) : IRequest<CustomerSummaryResponseModel>;

public class GetCustomerSummaryHandler(AppDbContext db)
    : IRequestHandler<GetCustomerSummaryQuery, CustomerSummaryResponseModel>
{
    public async Task<CustomerSummaryResponseModel> Handle(GetCustomerSummaryQuery request, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Customer {request.Id} not found.");

        var estimateCount = await db.Quotes
            .CountAsync(e => e.CustomerId == request.Id && e.DeletedAt == null
                && e.Type == QuoteType.Estimate
                && (e.Status == QuoteStatus.Draft || e.Status == QuoteStatus.Sent), ct);

        var quoteCount = await db.Quotes
            .CountAsync(q => q.CustomerId == request.Id && q.DeletedAt == null
                && q.Type == QuoteType.Quote
                && (q.Status == QuoteStatus.Draft || q.Status == QuoteStatus.Sent), ct);

        var orderCount = await db.SalesOrders
            .CountAsync(o => o.CustomerId == request.Id && o.DeletedAt == null
                && o.Status != SalesOrderStatus.Completed && o.Status != SalesOrderStatus.Cancelled, ct);

        var activeJobCount = await db.Jobs
            .CountAsync(j => j.CustomerId == request.Id && j.DeletedAt == null
                && !j.IsArchived, ct);

        var openInvoiceData = await db.Invoices
            .Where(i => i.CustomerId == request.Id && i.DeletedAt == null
                && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Overdue))
            .Select(i => new
            {
                Total = i.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + i.TaxRate)
            })
            .ToListAsync(ct);

        var ytdRevenue = await db.Payments
            .Where(p => p.CustomerId == request.Id && p.DeletedAt == null
                && p.PaymentDate.Year == DateTimeOffset.UtcNow.Year)
            .SumAsync(p => (decimal?)p.Amount ?? 0, ct);

        return new CustomerSummaryResponseModel(
            customer.Id,
            customer.Name,
            customer.CompanyName,
            customer.Email,
            customer.Phone,
            customer.IsActive,
            customer.ExternalId,
            customer.ExternalRef,
            customer.Provider,
            customer.CreatedAt,
            customer.UpdatedAt,
            estimateCount,
            quoteCount,
            orderCount,
            activeJobCount,
            openInvoiceData.Count,
            openInvoiceData.Sum(i => i.Total),
            ytdRevenue,
            customer.IsFdaRegulated,
            customer.IsAerospace,
            customer.IsAutomotive,
            customer.IsItarControlled,
            customer.IsReferenceOk,
            customer.ReferenceNotes);
    }
}
