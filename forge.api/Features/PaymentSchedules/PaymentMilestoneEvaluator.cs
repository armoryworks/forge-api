using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Api.Features.PaymentSchedules;

/// <summary>
/// THE single home for milestone due-ness + amount derivation (S2; S4c reuses this).
///
/// <para><strong>Due-ness is computed on read.</strong> There is no event plumbing in
/// the MVP: a trigger-based milestone's persisted status stays <c>Pending</c> and is
/// promoted to <c>Due</c> here, by inspecting the linked documents' current state.
/// The persisted status is authoritative only once money is involved
/// (<c>Invoiced</c>/<c>PartiallyPaid</c>/<c>Paid</c>/<c>Waived</c>) or when it was
/// manually overridden to <c>Due</c>.</para>
///
/// <para><strong>Amounts derive from percentages.</strong> Percentages are the source
/// of truth (Σ = 100); the due amount is percentage × the live document total —
/// except when <c>AmountLocked</c> is set (frozen at first invoice/payment), which
/// always wins.</para>
/// </summary>
public static class PaymentMilestoneEvaluator
{
    /// <summary>
    /// Commercial money rounding: 2 dp, half away from zero. Mirrors the canonical
    /// quantize used by <c>Invoice.BalanceDue</c> (F-027) — NOT .NET's default
    /// banker's rounding, which diverges on half-cents. That helper is private to
    /// the Invoice entity, so the formula is restated here verbatim; do not add a
    /// third copy — reuse this one from payment-schedule code.
    /// </summary>
    public static decimal QuantizeMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The amount due for a milestone: the locked amount when set (invoiced/paid
    /// milestones must not drift with later document edits), otherwise
    /// percentage × live document total, quantized.
    /// </summary>
    public static decimal DeriveAmount(PaymentMilestone milestone, decimal documentTotal)
        => milestone.AmountLocked ?? QuantizeMoney(documentTotal * milestone.Percentage / 100m);

    /// <summary>
    /// Effective (computed) status: the persisted status, except that a persisted
    /// <c>Pending</c> is promoted to <c>Due</c> when the milestone's trigger
    /// condition holds against the linked quote / sales order.
    /// </summary>
    public static PaymentMilestoneStatus EffectiveStatus(
        PaymentMilestone milestone, Quote? quote, SalesOrder? salesOrder, DateTimeOffset now)
        => milestone.Status == PaymentMilestoneStatus.Pending
            && IsTriggerSatisfied(milestone, quote, salesOrder, now)
                ? PaymentMilestoneStatus.Due
                : milestone.Status;

    /// <summary>
    /// Trigger → document-state mapping (the ONE place this lives):
    /// <list type="bullet">
    /// <item>OnAcceptance — quote Accepted/ConvertedToOrder (a linked SO with no quote implies acceptance)</item>
    /// <item>OnOrderConfirmation — SO Confirmed or later</item>
    /// <item>OnProductionStart — SO InProduction or later</item>
    /// <item>OnShipment — SO PartiallyShipped/Shipped/Completed</item>
    /// <item>OnDelivery — SO Completed</item>
    /// <item>FixedDate — DueDate ≤ now</item>
    /// <item>NetDays — (quote AcceptedDate ?? SO ConfirmedDate) + NetDays ≤ now</item>
    /// </list>
    /// SO-state triggers use the <see cref="SalesOrderStatus"/> enum order for the
    /// "or later" comparisons; a Cancelled order never makes a milestone due.
    /// </summary>
    public static bool IsTriggerSatisfied(
        PaymentMilestone milestone, Quote? quote, SalesOrder? salesOrder, DateTimeOffset now)
    {
        var soStatus = salesOrder is { Status: not SalesOrderStatus.Cancelled } ? salesOrder.Status : (SalesOrderStatus?)null;

        return milestone.DueTrigger switch
        {
            PaymentDueTrigger.OnAcceptance =>
                quote?.Status is QuoteStatus.Accepted or QuoteStatus.ConvertedToOrder
                || (quote is null && soStatus is not null),
            PaymentDueTrigger.OnOrderConfirmation => soStatus >= SalesOrderStatus.Confirmed,
            PaymentDueTrigger.OnProductionStart => soStatus >= SalesOrderStatus.InProduction,
            PaymentDueTrigger.OnShipment => soStatus >= SalesOrderStatus.PartiallyShipped,
            PaymentDueTrigger.OnDelivery => soStatus == SalesOrderStatus.Completed,
            PaymentDueTrigger.FixedDate => milestone.DueDate is { } due && due <= now,
            PaymentDueTrigger.NetDays =>
                milestone.NetDays is { } netDays
                && (quote?.AcceptedDate ?? salesOrder?.ConfirmedDate) is { } anchor
                && anchor.AddDays(netDays) <= now,
            _ => false,
        };
    }
}
