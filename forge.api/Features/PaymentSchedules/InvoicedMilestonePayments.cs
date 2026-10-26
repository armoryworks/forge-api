using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// Keeps an invoiced milestone's paid amount equal to the payments applied to its
/// invoice. The money is recorded once, on the invoice, and the payment schedule
/// follows it; recomputing from the applications (rather than adding the latest
/// payment) keeps the milestone right after a payment is voided as well.
/// </summary>
public static class InvoicedMilestonePayments
{
    /// <summary>
    /// Recomputes PaidAmount / Status for every non-waived milestone billed on one of
    /// <paramref name="invoiceIds"/> and logs each change on the schedule's document.
    /// Adds to the change tracker only; the caller saves when this returns true.
    /// </summary>
    public static async Task<bool> RefreshAsync(
        AppDbContext db,
        IReadOnlyCollection<int> invoiceIds,
        DateTimeOffset paidAt,
        string? paidReference,
        CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return false;

        var milestones = await db.PaymentMilestones
            .Include(m => m.PaymentSchedule)
            .Where(m => m.InvoiceId != null
                && invoiceIds.Contains(m.InvoiceId.Value)
                && m.Status != PaymentMilestoneStatus.Waived)
            .ToListAsync(ct);
        if (milestones.Count == 0) return false;

        var milestoneInvoiceIds = milestones.Select(m => m.InvoiceId!.Value).Distinct().ToList();
        var paidByInvoice = await db.PaymentApplications
            .Where(a => milestoneInvoiceIds.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Paid = g.Sum(a => a.Amount + a.DiscountAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Paid, ct);

        var changed = false;
        foreach (var milestone in milestones)
        {
            var paid = PaymentMilestoneEvaluator.QuantizeMoney(paidByInvoice.GetValueOrDefault(milestone.InvoiceId!.Value));
            var previous = milestone.PaidAmount ?? 0m;
            var status = paid <= 0m
                ? PaymentMilestoneStatus.Invoiced
                : paid >= (milestone.AmountLocked ?? paid)
                    ? PaymentMilestoneStatus.Paid
                    : PaymentMilestoneStatus.PartiallyPaid;

            if (paid == previous && status == milestone.Status) continue;

            if (paid > previous)
            {
                milestone.PaidAt = paidAt;
                if (paidReference is not null) milestone.PaidReference = paidReference;
            }
            else if (paid <= 0m)
            {
                milestone.PaidAt = null;
                milestone.PaidReference = null;
            }

            milestone.PaidAmount = paid > 0m ? paid : null;
            milestone.Status = status;

            var schedule = milestone.PaymentSchedule;
            var indexingPoint = schedule.SalesOrderId is int soId
                ? ("SalesOrder", soId)
                : ("Quote", schedule.QuoteId!.Value);
            db.LogActivityAt(
                paid > previous ? "payment-milestone-paid" : "payment-milestone-payment-reversed",
                $"Milestone {milestone.Sequence} '{milestone.Name}' paid {paid:0.00} from its invoice — {status}",
                indexingPoint);
            changed = true;
        }

        return changed;
    }
}
