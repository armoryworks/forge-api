using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPurchaseOrderRepository
{
    /// <summary>
    /// Legacy (non-paged) list. Kept for any caller that still needs the full
    /// flat array. New work should call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<PurchaseOrderListItemModel>> GetAllAsync(int? vendorId, int? jobId, PurchaseOrderStatus? status, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-broad / WU-22 standard contract. Returns
    /// the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<PurchaseOrderListItemModel>> GetPagedAsync(PurchaseOrderListQuery query, CancellationToken ct);

    Task<PurchaseOrder?> FindAsync(int id, CancellationToken ct);
    Task<PurchaseOrder?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<PurchaseOrderLine?> FindLineAsync(int lineId, CancellationToken ct);
    Task<string> GenerateNextPONumberAsync(CancellationToken ct);

    /// <summary>True when <paramref name="poNumber"/> is already used by another PO (excluding <paramref name="excludeId"/>).</summary>
    Task<bool> PONumberExistsAsync(string poNumber, int? excludeId, CancellationToken ct);
    Task AddAsync(PurchaseOrder po, CancellationToken ct);
    Task AddReceivingRecordAsync(ReceivingRecord record, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
