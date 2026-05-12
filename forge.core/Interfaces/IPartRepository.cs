using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPartRepository
{
    /// <summary>
    /// Legacy (non-paged) list. Kept for any internal caller that still needs
    /// the full flat array. New work should call <see cref="GetPagedAsync"/>.
    /// </summary>
    Task<List<PartListResponseModel>> GetPartsAsync(PartStatus? status, string? search, CancellationToken ct);

    /// <summary>
    /// Paged list per the Phase 3 F7-partial / WU-17 standard contract.
    /// Returns the slice + the total matching count for pagination UI.
    /// </summary>
    Task<PagedResponse<PartListResponseModel>> GetPagedAsync(PartListQuery query, CancellationToken ct);

    Task<PartDetailResponseModel?> GetDetailAsync(int id, CancellationToken ct);
    Task<Part?> FindAsync(int id, CancellationToken ct);
    Task<bool> PartNumberExistsAsync(string partNumber, int? excludeId, CancellationToken ct);
    Task<string> GetNextPartNumberAsync(InventoryClass inventoryClass, CancellationToken ct);
    Task AddAsync(Part part, CancellationToken ct);
    Task<BOMEntry?> FindBomEntryAsync(int bomEntryId, int parentPartId, CancellationToken ct);
    Task<int> GetMaxBomSortOrderAsync(int parentPartId, CancellationToken ct);
    Task AddBomEntryAsync(BOMEntry entry, CancellationToken ct);
    Task RemoveBomEntryAsync(BOMEntry entry);
    Task<List<OperationResponseModel>> GetOperationsAsync(int partId, CancellationToken ct);
    Task<Operation?> FindOperationAsync(int operationId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
