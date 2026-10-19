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
                .Include(o => o.Lines)
                .Include(o => o.Invoices)
                .FirstOrDefaultAsync(o => o.Id == soId, ct);
            if (so is null || so.Status != SalesOrderStatus.Shipped) continue;
            if (so.Lines.Count == 0 || !so.Lines.All(l => l.IsFullyShipped)) continue;
            var billable = so.Invoices.Where(i => i.Status != InvoiceStatus.Voided).ToList();
            if (billable.Count == 0 || !billable.All(i => i.Status == InvoiceStatus.Paid)) continue;
            so.Status = SalesOrderStatus.Completed;
            anyCompleted = true;
        }
        if (anyCompleted) await db.SaveChangesAsync(ct);
    }
}
