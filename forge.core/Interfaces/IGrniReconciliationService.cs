using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Read seam for the Phase-2 STAGE D.3 GRNI (Goods-Received-Not-Invoiced) reconciliation + aging report.
/// Ties the GL GRNI account balance to the operational received-but-not-yet-billed position and ages the
/// open accrual by receipt date. Read-only; touches no operational state.
/// </summary>
public interface IGrniReconciliationService
{
    Task<GrniReconciliation> GetGrniReconciliationAsync(int bookId, DateOnly? asOfDate = null, CancellationToken ct = default);
}
