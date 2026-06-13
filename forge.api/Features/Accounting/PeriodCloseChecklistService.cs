using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class PeriodCloseChecklistService(
    IGrniReconciliationService grni,
    IArAgingService arAging,
    IApAgingService apAging) : IPeriodCloseChecklistService
{
    public async Task<CloseChecklistResult> EvaluateAsync(int bookId, DateOnly asOf, CancellationToken ct = default)
    {
        var items = new List<CloseChecklistItem>();

        // GRNI: GL vs operational received-not-billed must tie (no stranded/unposted accrual).
        var grniRecon = await grni.GetGrniReconciliationAsync(bookId, asOf, ct);
        items.Add(new CloseChecklistItem(
            "GRNI_RECONCILED", "GRNI ties (GL vs received-not-billed)",
            grniRecon.IsReconciled,
            grniRecon.IsReconciled ? "Reconciled" : $"Variance {grniRecon.Variance:0.00}"));

        // AR sub-ledger ties to the AR control account.
        var ar = await arAging.ReconcileAsync(bookId, asOf, ct);
        items.Add(new CloseChecklistItem(
            "AR_TIES", "AR aging ties to control",
            ar.IsReconciled, ar.IsReconciled ? "Reconciled" : $"Difference {ar.Difference:0.00}"));

        // AP sub-ledger ties to the AP control account.
        var ap = await apAging.ReconcileAsync(bookId, asOf, ct);
        items.Add(new CloseChecklistItem(
            "AP_TIES", "AP aging ties to control",
            ap.IsReconciled, ap.IsReconciled ? "Reconciled" : $"Difference {ap.Difference:0.00}"));

        return new CloseChecklistResult(bookId, asOf, items);
    }
}
