using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IVendorRepository
{
    Task<List<VendorResponseModel>> GetAllActiveAsync(CancellationToken ct);

    /// <summary>
    /// Legacy (non-paged) list. Kept for any caller that still needs the full
    /// flat array. New work should call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<VendorListItemModel>> GetAllAsync(string? search, bool? isActive, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-broad / WU-22 standard contract. Returns
    /// the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<VendorListItemModel>> GetPagedAsync(VendorListQuery query, CancellationToken ct);

    Task<Vendor?> FindAsync(int id, CancellationToken ct);
    Task<Vendor?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task AddAsync(Vendor vendor, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
