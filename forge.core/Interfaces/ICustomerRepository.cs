using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICustomerRepository
{
    Task<List<CustomerResponseModel>> GetAllActiveAsync(CancellationToken ct);

    /// <summary>
    /// Legacy (non-paged) list. Kept for any caller that still needs the full
    /// flat array; the dropdown / unfiltered helpers use this. New work should
    /// call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<CustomerListItemModel>> GetAllAsync(string? search, bool? isActive, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-partial / WU-17 standard contract.
    /// Returns the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<CustomerListItemModel>> GetPagedAsync(CustomerListQuery query, CancellationToken ct);

    Task<Customer?> FindAsync(int id, CancellationToken ct);
    Task<Customer?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task AddAsync(Customer customer, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
