namespace Forge.Core.Models;

/// <summary>
/// Rollup totals for a payment schedule, derived at read time from the live
/// document total (SalesOrder.Total when linked, else Quote.Total).
/// RemainingTotal = Σ non-waived milestone amounts − PaidTotal.
/// </summary>
public record PaymentScheduleTotalsModel(
    decimal DocumentTotal,
    decimal PaidTotal,
    decimal RemainingTotal);
