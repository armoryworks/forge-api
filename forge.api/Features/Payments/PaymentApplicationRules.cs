using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payments;

internal static class PaymentApplicationRules
{
    public static async Task ApplyToInvoiceAsync(
        AppDbContext db,
        Payment payment,
        Invoice invoice,
        CreatePaymentApplicationModel app,
        ISet<int> completionCandidateSoIds,
        CancellationToken ct)
    {
        if (invoice.CustomerId != payment.CustomerId)
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} belongs to a different customer");

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue))
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only Sent, PartiallyPaid, or Overdue invoices can be paid");

        var currencies = new DocumentCurrencyResolver(db);
        var paymentCurrencyId = await currencies.EffectiveCurrencyIdAsync(payment.CurrencyId, ct);
        var invoiceCurrencyId = await currencies.EffectiveCurrencyIdAsync(invoice.CurrencyId, ct);
        if (invoiceCurrencyId != paymentCurrencyId)
        {
            var codes = await db.Currencies.AsNoTracking()
                .Where(c => c.Id == invoiceCurrencyId || c.Id == paymentCurrencyId)
                .ToDictionaryAsync(c => c.Id, c => c.Code, ct);
            DocumentCurrencyResolver.EnsureSameCurrency(
                paymentCurrencyId, invoiceCurrencyId, $"Invoice {invoice.InvoiceNumber}",
                codes.GetValueOrDefault(paymentCurrencyId), codes.GetValueOrDefault(invoiceCurrencyId));
        }

        var balanceDue = invoice.BalanceDue;

        if (app.Amount > balanceDue)
        {
            var money = await MoneyFormat.ForCurrencyIdAsync(db, invoice.CurrencyId, ct);
            throw new InvalidOperationException(
                $"Application amount {money.Format(app.Amount)} exceeds invoice {invoice.InvoiceNumber} balance of {money.Format(balanceDue)}");
        }

        payment.Applications.Add(new PaymentApplication
        {
            InvoiceId = app.InvoiceId,
            Amount = app.Amount,
            SettlementFxRate = app.SettlementFxRate ?? payment.FxRate,
        });

        var newBalance = balanceDue - app.Amount;
        if (newBalance <= 0)
        {
            invoice.Status = InvoiceStatus.Paid;
            if (invoice.SalesOrderId is int paidSoId) completionCandidateSoIds.Add(paidSoId);
        }
        else if (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Overdue)
            invoice.Status = InvoiceStatus.PartiallyPaid;

        db.Entry(invoice).Property(i => i.Version).IsModified = true;
    }

    public static async Task CompletePaidSalesOrdersAsync(
        AppDbContext db, IReadOnlyCollection<int> salesOrderIds, CancellationToken ct)
    {
        if (salesOrderIds.Count == 0) return;

        var anyCompleted = false;
        foreach (var soId in salesOrderIds)
        {
            var so = await db.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Part)
                .Include(o => o.Invoices).ThenInclude(i => i.Lines)
                .FirstOrDefaultAsync(o => o.Id == soId, ct);
            if (so is null || so.Lines.Count == 0) continue;
            var shippedLines = so.Lines.Where(l => l.ShipsGoods).ToList();
            var reachedFulfilment = shippedLines.Count > 0
                ? so.Status == SalesOrderStatus.Shipped
                : so.Status is SalesOrderStatus.Confirmed or SalesOrderStatus.InProduction
                    or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped;
            if (!reachedFulfilment || !shippedLines.All(l => l.IsFullyShipped)) continue;
            var billable = so.Invoices.Where(i => i.Status != InvoiceStatus.Voided).ToList();
            if (billable.Count == 0 || !billable.All(i => i.Status == InvoiceStatus.Paid)) continue;
            if (!ServiceLinesInvoiced(so, billable)) continue;
            so.Status = SalesOrderStatus.Completed;
            anyCompleted = true;
        }
        if (anyCompleted) await db.SaveChangesAsync(ct);
    }

    private static bool ServiceLinesInvoiced(SalesOrder so, List<Invoice> billable)
    {
        var invoicedByPart = billable
            .SelectMany(i => i.Lines)
            .Where(l => l.PartId != null)
            .GroupBy(l => l.PartId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        return so.Lines
            .Where(l => !l.ShipsGoods && !l.IsFreight)
            .GroupBy(l => l.PartId!.Value)
            .All(g => invoicedByPart.GetValueOrDefault(g.Key) >= g.Sum(l => l.Quantity));
    }
}
