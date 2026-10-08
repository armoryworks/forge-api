using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IInventoryRepository
{
    // Locations
    Task<List<StorageLocationResponseModel>> GetLocationTreeAsync(CancellationToken ct);
    Task<List<StorageLocationFlatResponseModel>> GetBinLocationsAsync(CancellationToken ct);
    Task<PagedResponse<StorageLocationFlatResponseModel>> GetBinLocationsPagedAsync(
        string? search, int page, int pageSize, bool activeOnly, CancellationToken ct);
    Task<StorageLocation?> FindLocationAsync(int id, CancellationToken ct);
    Task<List<StorageLocation>> GetStorageLocationsAsync(CancellationToken ct);
    Task<bool> BarcodeExistsAsync(string barcode, int? excludeId, CancellationToken ct);
    Task AddLocationAsync(StorageLocation location, CancellationToken ct);

    /// <summary>Existence check for a non-deleted Part — used by the friendly stock
    /// verbs (receive-stock/use-stock) to reject an unknown partId before writing
    /// bin content for it. See inventory.md B38.</summary>
    Task<bool> PartExistsAsync(int partId, CancellationToken ct);

    /// <summary>
    /// Returns the single default storage location, creating a "Main" bin if none
    /// exists. Used by single-location mode so manual stock can be tracked without
    /// the customer choosing a location. Idempotent.
    /// </summary>
    Task<StorageLocation> EnsureDefaultLocationAsync(CancellationToken ct);

    // Bin contents
    Task<List<BinContentResponseModel>> GetBinContentsAsync(int locationId, CancellationToken ct);
    Task<BinContent?> FindBinContentAsync(int id, CancellationToken ct);
    /// <summary>Every active (not removed) bin content for a part at a location, un-lotted first and then
    /// oldest placed first — the draw-down order for stock-outs and counts across lots.</summary>
    Task<List<BinContent>> GetActiveBinContentsByPartLocationAsync(int partId, int locationId, CancellationToken ct);
    /// <summary>Active (not removed) bin content for a part at a location holding the given
    /// lot (null matches only un-lotted content) — used by PO receiving so lots never blend.</summary>
    Task<BinContent?> FindActiveBinContentByPartLocationLotAsync(int partId, int locationId, string? lotNumber, CancellationToken ct);
    /// <summary>The number of the newest NCR still holding the part's lot on quality hold, or null when
    /// none does — names the NCR when a stock-out is refused for held stock.</summary>
    Task<string?> FindQualityHoldNcrNumberAsync(int partId, string? lotNumber, CancellationToken ct);
    Task AddBinContentAsync(BinContent content, CancellationToken ct);
    Task AddMovementAsync(BinMovement movement, CancellationToken ct);

    // Inventory summary
    Task<List<InventoryPartSummaryResponseModel>> GetPartInventorySummaryAsync(string? search, PartStatus? status, CancellationToken ct);

    // Movement history
    Task<List<BinMovementResponseModel>> GetMovementsAsync(int? locationId, string? entityType, int? entityId, int take, CancellationToken ct);

    // Receiving
    Task<List<ReceivingRecordResponseModel>> GetReceivingHistoryAsync(int? purchaseOrderId, int? partId, int take, CancellationToken ct);

    // Transfer / Adjust
    Task<BinContent?> FindBinContentWithLocationAsync(int id, CancellationToken ct);

    // Cycle counts
    Task<CycleCount?> FindCycleCountAsync(int id, CancellationToken ct);
    Task<List<CycleCountResponseModel>> GetCycleCountsAsync(int? locationId, string? status, CancellationToken ct);
    Task AddCycleCountAsync(CycleCount cycleCount, CancellationToken ct);

    // Reservations
    Task<List<ReservationResponseModel>> GetReservationsAsync(int? partId, int? jobId, CancellationToken ct);
    Task<Reservation?> FindReservationAsync(int id, CancellationToken ct);
    Task AddReservationAsync(Reservation reservation, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
