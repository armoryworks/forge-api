using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IInvoiceRepository
{
    /// <summary>
    /// Legacy (non-paged) list. Kept for any caller that still needs the full
    /// flat array. New work should call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<InvoiceListItemModel>> GetAllAsync(int? customerId, InvoiceStatus? status, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-broad / WU-22 standard contract. Returns
    /// the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<InvoiceListItemModel>> GetPagedAsync(InvoiceListQuery query, CancellationToken ct);

    Task<Invoice?> FindAsync(int id, CancellationToken ct);
    Task<Invoice?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct);
    Task AddAsync(Invoice invoice, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
