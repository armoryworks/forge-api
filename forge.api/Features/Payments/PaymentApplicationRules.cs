using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payments;

internal static class PaymentApplicationRules
{
    public static void ApplyToInvoice(
        AppDbContext db,
        Payment payment,
        Invoice invoice,
        CreatePaymentApplicationModel app,
        ISet<int> completionCandidateSoIds)
    {
        if (invoice.CustomerId != payment.CustomerId)
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} belongs to a different customer");

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue))
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only Sent, PartiallyPaid, or Overdue invoices can be paid");

        var balanceDue = invoice.BalanceDue;

        if (app.Amount > balanceDue)
            throw new InvalidOperationException(
                $"Application amount {app.Amount:C} exceeds invoice {invoice.InvoiceNumber} balance of {balanceDue:C}");

        payment.Applications.Add(new PaymentApplication
        {
            InvoiceId = app.InvoiceId,
            Amount = app.Amount,
            SettlementFxRate = app.SettlementFxRate,
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
            .Where(l => !l.ShipsGoods)
            .GroupBy(l => l.PartId!.Value)
            .All(g => invoicedByPart.GetValueOrDefault(g.Key) >= g.Sum(l => l.Quantity));
    }
}
