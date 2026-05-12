using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPriceListRepository
{
    Task<List<PriceListListItemModel>> GetAllAsync(int? customerId, CancellationToken ct);
    Task<PriceList?> FindAsync(int id, CancellationToken ct);
    Task<PriceList?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task AddAsync(PriceList priceList, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// Clears <c>IsDefault</c> on every other (non-deleted) price list in
    /// the same scope so the unique-default invariant holds. When
    /// <paramref name="customerId"/> is null the scope is system-wide; when
    /// set, the scope is the customer's own list pool. The optional
    /// <paramref name="excludePriceListId"/> avoids round-tripping the row
    /// we're about to flip on (used during update).
    /// </summary>
    Task UnsetDefaultForScopeAsync(int? customerId, int? excludePriceListId, CancellationToken ct);

    // Entry-level CRUD (introduced for the PriceListEntry edit UI).
    Task<bool> PriceListExistsAsync(int priceListId, CancellationToken ct);
    Task<PagedResponse<PriceListEntryResponseModel>> GetEntriesAsync(
        int priceListId, string? search, int page, int pageSize, CancellationToken ct);
    Task<PriceListEntry?> FindEntryAsync(int entryId, CancellationToken ct);
    Task<PriceListEntry?> FindEntryWithPartAsync(int entryId, CancellationToken ct);
    Task AddEntryAsync(PriceListEntry entry, CancellationToken ct);
}
