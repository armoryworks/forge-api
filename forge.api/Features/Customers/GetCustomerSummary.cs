using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Services.Fx;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Customers;

public record GetCustomerSummaryQuery(int Id) : IRequest<CustomerSummaryResponseModel>;

public class GetCustomerSummaryHandler(AppDbContext db, IClock clock)
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

        var exposure = await CustomerCreditExposure.ForCustomerAsync(db, request.Id, ct);

        var yearStart = new DateTimeOffset(clock.UtcNow.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ytdRevenue = await db.Invoices
            .Where(i => i.CustomerId == request.Id
                && i.Status != InvoiceStatus.Draft
                && i.Status != InvoiceStatus.Voided
                && i.InvoiceDate >= yearStart
                && i.InvoiceDate < yearStart.AddYears(1))
            .SelectMany(i => i.Lines)
            .SumAsync(l => (decimal?)(l.Quantity * l.UnitPrice) ?? 0m, ct);

        return new CustomerSummaryResponseModel(
            customer.Id,
            customer.Name,
            customer.CustomerNumber,
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
            exposure.OpenInvoiceCount,
            exposure.OpenArBalance,
            ytdRevenue,
            customer.IsFdaRegulated,
            customer.IsAerospace,
            customer.IsAutomotive,
            customer.IsItarControlled,
            customer.IsReferenceOk,
            customer.ReferenceNotes,
            customer.CreditLimit,
            customer.IsTaxExempt,
            customer.TaxExemptionId,
            customer.Version,
            customer.DefaultCreditTerms?.ToString(),
            customer.IsOnCreditHold,
            customer.CurrencyId,
            await PartyCurrency.CustomerHasPostedDocumentsAsync(db, customer.Id, ct),
            customer.FreightTerms,
            customer.PaymentTermsId,
            await new PaymentTermsResolver(db).ModelAsync(customer.PaymentTermsId, customer.DefaultCreditTerms, ct),
            customer.DefaultTaxCodeId);
    }
}
