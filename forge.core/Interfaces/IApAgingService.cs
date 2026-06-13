using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Read seam for the Accounts-Payable aging report (AP counterpart of <c>IArAgingService</c>).
/// Filter-immune projection over posted AP-control journal lines, by vendor party.
/// </summary>
public interface IApAgingService
{
    Task<ApAging> GetApAgingAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default);
    Task<ApAgingReconciliation> ReconcileAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default);
}
