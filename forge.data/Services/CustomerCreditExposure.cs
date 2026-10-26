using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Data.Services;

/// <summary>
/// A customer's open receivables and the uninvoiced part of its open sales orders.
/// </summary>
/// <param name="OpenArBalance">Sum of the balance due on every sent, unpaid invoice.</param>
/// <param name="OpenInvoiceCount">Number of invoices counted in <paramref name="OpenArBalance"/>.</param>
/// <param name="PendingOrdersTotal">Value of open sales orders not yet billed on a sent invoice.</param>
public sealed record CustomerExposure(decimal OpenArBalance, int OpenInvoiceCount, decimal PendingOrdersTotal)
{
    public decimal TotalExposure => OpenArBalance + PendingOrdersTotal;
}

/// <summary>
/// The one credit-exposure formula shared by the credit status card, the credit risk
/// report and the customer summary tiles.
/// <list type="bullet">
/// <item>Open AR counts only invoices that have been issued (not Draft, Voided or Paid),
/// each at the canonical <see cref="Invoice.BalanceDue"/> rule: quantized total less payments.</item>
/// <item>Pending orders counts each open sales order at its total less the issued
/// (non-Draft, non-Voided) invoices raised against it, floored at zero, so a billed
/// amount is never counted twice and a drafted invoice's amount stays in pending.</item>
/// </list>
/// <see cref="Invoice.BalanceDue"/> is not mapped, so the money is projected from lines and
/// payment applications and finished in memory.
/// </summary>
public static class CustomerCreditExposure
{
    public static async Task<CustomerExposure> ForCustomerAsync(AppDbContext db, int customerId, CancellationToken ct)
    {
        var map = await ForCustomersAsync(db, [customerId], ct);
        return map[customerId];
    }

    public static async Task<Dictionary<int, CustomerExposure>> ForCustomersAsync(
        AppDbContext db, IReadOnlyCollection<int> customerIds, CancellationToken ct)
    {
        var openInvoices = await db.Invoices
            .AsNoTracking()
            .Where(i => customerIds.Contains(i.CustomerId)
                && i.Status != InvoiceStatus.Draft
                && i.Status != InvoiceStatus.Voided
                && i.Status != InvoiceStatus.Paid)
            .Select(i => new
            {
                i.CustomerId,
                LineTotal = i.Lines.Sum(l => l.Quantity * l.UnitPrice),
                TaxableLineTotal = i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => l.Quantity * l.UnitPrice),
                i.TaxRate,
                Paid = i.PaymentApplications.Sum(pa => pa.Amount + pa.DiscountAmount),
            })
            .ToListAsync(ct);

        var orders = await db.SalesOrders
            .AsNoTracking()
            .Where(so => customerIds.Contains(so.CustomerId)
                && so.Status != SalesOrderStatus.Cancelled
                && so.Status != SalesOrderStatus.Completed)
            .Select(so => new
            {
                so.Id,
                so.CustomerId,
                LineTotal = so.Lines.Sum(l => l.Quantity * l.UnitPrice),
                so.TaxRate,
            })
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var billedRows = await db.Invoices
            .AsNoTracking()
            .Where(i => i.SalesOrderId != null && orderIds.Contains(i.SalesOrderId.Value)
                && i.Status != InvoiceStatus.Draft
                && i.Status != InvoiceStatus.Voided)
            .Select(i => new
            {
                SalesOrderId = i.SalesOrderId!.Value,
                LineTotal = i.Lines.Sum(l => l.Quantity * l.UnitPrice),
                TaxableLineTotal = i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => l.Quantity * l.UnitPrice),
                i.TaxRate,
            })
            .ToListAsync(ct);

        var billedByOrder = billedRows
            .GroupBy(r => r.SalesOrderId)
            .ToDictionary(g => g.Key, g => g.Sum(r => Quantize(r.LineTotal + r.TaxableLineTotal * r.TaxRate)));

        return customerIds.Distinct().ToDictionary(id => id, id =>
        {
            var invoices = openInvoices.Where(i => i.CustomerId == id).ToList();
            var openAr = invoices.Sum(i => Quantize(i.LineTotal + i.TaxableLineTotal * i.TaxRate) - i.Paid);
            var pending = orders
                .Where(o => o.CustomerId == id)
                .Sum(o => Math.Max(0m, o.LineTotal * (1 + o.TaxRate) - billedByOrder.GetValueOrDefault(o.Id)));
            return new CustomerExposure(openAr, invoices.Count, pending);
        });
    }

    private static decimal Quantize(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
