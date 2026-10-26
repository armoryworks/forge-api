using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// The payment run: approved bills with a balance that fall due by <c>DueThrough</c> (default the pay date) or
/// whose early-payment discount is still open on the pay date. Each comes with the suggested cash, the discount it
/// earns and the days left to take it, soonest discount deadline first.
/// </summary>
public record GetVendorPaymentSuggestionsQuery(
    DateTimeOffset PayDate,
    DateTimeOffset? DueThrough = null,
    int? VendorId = null,
    int? CurrencyId = null) : IRequest<List<VendorPaymentSuggestionModel>>;

public class GetVendorPaymentSuggestionsHandler(AppDbContext db)
    : IRequestHandler<GetVendorPaymentSuggestionsQuery, List<VendorPaymentSuggestionModel>>
{
    public async Task<List<VendorPaymentSuggestionModel>> Handle(
        GetVendorPaymentSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var discounts = await DiscountSettings.ReadAsync(db, cancellationToken);
        var dueThrough = (request.DueThrough ?? request.PayDate).ToOffset(request.PayDate.Offset).Date;

        var query = db.VendorBills.AsNoTracking()
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .Where(b => b.Status == VendorBillStatus.Approved || b.Status == VendorBillStatus.PartiallyPaid);
        if (request.VendorId is int vendorId)
            query = query.Where(b => b.VendorId == vendorId);
        if (request.CurrencyId is int currencyId)
            query = query.Where(b => b.CurrencyId == currencyId);

        var bills = await query.ToListAsync(cancellationToken);
        var vendorIds = bills.Select(b => b.VendorId).Distinct().ToList();
        var vendorNames = await db.Vendors.AsNoTracking().IgnoreQueryFilters()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.CompanyName, cancellationToken);
        var currencyCodes = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
        var terms = await new PaymentTermsResolver(db).ModelsAsync(bills.Select(b => b.PaymentTermsId), cancellationToken);

        var suggestions = new List<VendorPaymentSuggestionModel>();
        foreach (var bill in bills)
        {
            var balance = bill.BalanceDue;
            if (balance <= 0m)
                continue;

            var discount = EarlyPaymentDiscountRules.AvailableOn(bill, request.PayDate, discounts);
            var dueByCutoff = bill.DueDate.ToOffset(request.PayDate.Offset).Date <= dueThrough;
            if (!dueByCutoff && discount <= 0m)
                continue;

            var deadline = discount > 0m ? EarlyPaymentDiscount.Deadline(bill.DiscountDueDate, discounts.GraceDays) : null;
            suggestions.Add(new VendorPaymentSuggestionModel(
                bill.Id, bill.BillNumber, bill.VendorId,
                vendorNames.GetValueOrDefault(bill.VendorId) ?? $"Vendor {bill.VendorId}",
                bill.VendorInvoiceNumber, bill.CurrencyId, currencyCodes.GetValueOrDefault(bill.CurrencyId),
                bill.BillDate, bill.DueDate, balance, balance - discount, discount, deadline,
                deadline is { } d ? (d.ToOffset(request.PayDate.Offset).Date - request.PayDate.Date).Days : null,
                bill.PaymentTermsId is int termsId ? terms.GetValueOrDefault(termsId)?.Name : null));
        }

        return suggestions
            .OrderBy(s => s.DiscountDeadline is null)
            .ThenBy(s => s.DiscountDeadline)
            .ThenBy(s => s.DueDate)
            .ThenBy(s => s.BillNumber, StringComparer.Ordinal)
            .ToList();
    }
}
