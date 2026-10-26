namespace Forge.Api.Features.Invoices;

/// <summary>
/// A sales order's customer deposits in the order's currency: what its sent milestone invoices billed, the
/// functional value that was booked to Customer Deposits, and what its other sent invoices have used.
/// </summary>
public sealed record DepositPool(decimal Billed, decimal BilledFunctional, decimal Used, IReadOnlyList<string> DepositNumbers)
{
    /// <summary>Deposit still to be used by a later invoice.</summary>
    public decimal Remaining => Math.Max(0m, Billed - Used);

    /// <summary>Functional value per unit of deposit as booked: the weighted rate of the deposit invoices, 1 when none.</summary>
    public decimal BookedRate => Billed == 0m ? 1m : BilledFunctional / Billed;
}
