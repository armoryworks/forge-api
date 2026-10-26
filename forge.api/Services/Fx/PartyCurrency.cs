using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Services.Fx;

/// <summary>
/// Sets the trading currency of a customer or vendor. The currency can change until the party has a
/// non-draft document (an issued quote, order, invoice, PO or bill, or any payment); after that the change is
/// refused with <c>party-currency-locked</c>, the way QuickBooks locks a customer's currency.
/// </summary>
public static class PartyCurrency
{
    /// <summary>
    /// Applies a requested currency to <paramref name="customer"/>, by id or by ISO code (id wins), keeping the
    /// legacy <see cref="Customer.DefaultCurrency"/> code in step. Returns true when the currency changed.
    /// </summary>
    public static async Task<bool> ApplyToCustomerAsync(
        AppDbContext db, Customer customer, int? currencyId, string? code, bool clear, CancellationToken ct)
    {
        var currencies = new DocumentCurrencyResolver(db);
        var target = await TargetAsync(currencies, currencyId, code, clear, ct);
        if (target is null || await SameEffectiveAsync(currencies, target.Value.Id, customer.CurrencyId, ct))
        {
            if (target is { Code: { } sameCode } && customer.DefaultCurrency != sameCode)
                customer.DefaultCurrency = sameCode;
            return false;
        }

        if (customer.Id > 0 && await CustomerHasPostedDocumentsAsync(db, customer.Id, ct))
            throw new CurrencyLockedException(
                CurrencyLockedException.PartyLocked,
                $"{customer.Name} already has issued documents or payments, so its currency can't change.");

        customer.CurrencyId = target.Value.Id;
        customer.DefaultCurrency = target.Value.Code;
        return true;
    }

    /// <summary>Applies a requested currency to <paramref name="vendor"/>. Returns true when the currency changed.</summary>
    public static async Task<bool> ApplyToVendorAsync(
        AppDbContext db, Vendor vendor, int? currencyId, bool clear, CancellationToken ct)
    {
        var currencies = new DocumentCurrencyResolver(db);
        var target = await TargetAsync(currencies, currencyId, null, clear, ct);
        if (target is null || await SameEffectiveAsync(currencies, target.Value.Id, vendor.CurrencyId, ct))
            return false;

        if (vendor.Id > 0 && await VendorHasPostedDocumentsAsync(db, vendor.Id, ct))
            throw new CurrencyLockedException(
                CurrencyLockedException.PartyLocked,
                $"{vendor.CompanyName} already has issued purchase orders, bills or payments, so its currency can't change.");

        vendor.CurrencyId = target.Value.Id;
        return true;
    }

    /// <summary>True once the customer has a quote, order or invoice past Draft, or any payment.</summary>
    public static async Task<bool> CustomerHasPostedDocumentsAsync(AppDbContext db, int customerId, CancellationToken ct)
        => await db.Invoices.AnyAsync(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Draft, ct)
           || await db.Payments.AnyAsync(p => p.CustomerId == customerId, ct)
           || await db.SalesOrders.AnyAsync(o => o.CustomerId == customerId && o.Status != SalesOrderStatus.Draft, ct)
           || await db.Quotes.AnyAsync(q => q.CustomerId == customerId && q.Status != QuoteStatus.Draft, ct);

    /// <summary>True once the vendor has a purchase order or bill past Draft, or any payment.</summary>
    public static async Task<bool> VendorHasPostedDocumentsAsync(AppDbContext db, int vendorId, CancellationToken ct)
        => await db.PurchaseOrders.AnyAsync(p => p.VendorId == vendorId && p.Status != PurchaseOrderStatus.Draft, ct)
           || await db.VendorBills.AnyAsync(b => b.VendorId == vendorId && b.Status != VendorBillStatus.Draft, ct)
           || await db.VendorPayments.AnyAsync(p => p.VendorId == vendorId, ct);

    private static async Task<bool> SameEffectiveAsync(
        DocumentCurrencyResolver currencies, int? target, int? current, CancellationToken ct)
        => target == current
           || await currencies.EffectiveCurrencyIdAsync(target, ct) == await currencies.EffectiveCurrencyIdAsync(current, ct);

    private static async Task<(int? Id, string? Code)?> TargetAsync(
        DocumentCurrencyResolver currencies, int? currencyId, string? code, bool clear, CancellationToken ct)
    {
        if (clear)
            return (null, null);

        var id = currencyId ?? await currencies.CurrencyIdForCodeAsync(code, ct);
        if (id is null)
            return null;

        var resolved = await currencies.ResolveCurrencyAsync(id, null, ct);
        return (resolved.CurrencyId, resolved.Code);
    }
}
