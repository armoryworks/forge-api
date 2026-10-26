using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — portal invoice list. ⚡ Standalone Financial: returns
/// an empty list when an external accounting provider is connected, so
/// the customer doesn't see partial / divergent invoice data — the
/// integrated provider's portal is the source of truth in that mode. That
/// empty list is flagged as <see cref="PortalList{T}.ExternalAccounting"/>.
/// </summary>
public record GetPortalInvoicesQuery(int CustomerId) : IRequest<PortalList<PortalInvoiceListItem>>;

public class GetPortalInvoicesHandler(AppDbContext db, IAccountingService accounting, IClock? clock = null)
    : IRequestHandler<GetPortalInvoicesQuery, PortalList<PortalInvoiceListItem>>
{
    public async Task<PortalList<PortalInvoiceListItem>> Handle(GetPortalInvoicesQuery request, CancellationToken ct)
    {
        var standalone = string.Equals(accounting.ProviderId, "mock", StringComparison.OrdinalIgnoreCase);
        if (!standalone) return new([], 0, ExternalAccounting: true);

        var scope = db.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == request.CustomerId && i.Status != InvoiceStatus.Draft);
        var invoices = await scope
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Include(i => i.Currency)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(PortalReadRules.ListCap)
            .ToListAsync(ct);
        var totalCount = await PortalReadRules.TotalCountAsync(scope, invoices.Count, ct);
        var discounts = await DiscountSettings.ReadAsync(db, ct);
        var terms = await new PaymentTermsResolver(db).ModelsAsync(invoices.Select(i => i.PaymentTermsId), ct);

        return new(invoices.Select(i => new PortalInvoiceListItem(
            Id: i.Id,
            InvoiceNumber: i.InvoiceNumber,
            Status: i.Status.ToString(),
            InvoiceDate: i.InvoiceDate,
            DueDate: i.DueDate,
            Total: Math.Round(i.Total, 2, MidpointRounding.AwayFromZero),
            AmountPaid: i.AmountPaid,
            Balance: i.Status == InvoiceStatus.Voided ? 0m : i.BalanceDue,
            CurrencyCode: i.Currency?.Code,
            Freight: i.FreightSubtotal,
            TermsName: terms.GetValueOrDefault(i.PaymentTermsId ?? 0)?.Name,
            DiscountAvailable: i.Status == InvoiceStatus.Voided ? 0m : EarlyPaymentDiscountRules.AvailableOn(i, clock?.UtcNow ?? i.InvoiceDate, discounts),
            DiscountDeadline: EarlyPaymentDiscount.Deadline(i.DiscountDueDate, discounts.GraceDays))).ToList(), totalCount);
    }
}
