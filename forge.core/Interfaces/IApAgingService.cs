using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Read seam for the Accounts-Payable aging report (AP counterpart of <c>IArAgingService</c>).
/// Filter-immune projection over posted AP-control journal lines, by vendor party.
/// </summary>
public interface IApAgingService
{
    /// <summary>
    /// The AP aging as of a date. <paramref name="currencyId"/> limits the rows and buckets to bills in that
    /// currency; the reconciliation always covers every bill.
    /// </summary>
    Task<ApAging> GetApAgingAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default, int? currencyId = null);
    Task<ApAgingReconciliation> ReconcileAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default);
}
