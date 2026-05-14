using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class InventoryRepository(AppDbContext db) : IInventoryRepository
{
    public async Task<List<StorageLocationResponseModel>> GetLocationTreeAsync(CancellationToken ct)
    {
        var locations = await db.StorageLocations
            .Include(l => l.Contents.Where(c => c.RemovedAt == null))
            .Where(l => l.DeletedAt == null)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .ToListAsync(ct);

        var lookup = locations.ToLookup(l => l.ParentId);
        return BuildTree(null, lookup, "");
    }

    private static List<StorageLocationResponseModel> BuildTree(
        int? parentId,
        ILookup<int?, StorageLocation> lookup,
        string parentPath)
    {
        return lookup[parentId].Select(loc =>
        {
            var path = string.IsNullOrEmpty(parentPath) ? loc.Name : $"{parentPath} / {loc.Name}";
            var children = BuildTree(loc.Id, lookup, path);
            var contentCount = loc.Contents.Count + children.Sum(c => c.ContentCount);

            return new StorageLocationResponseModel(
                loc.Id, loc.Name, loc.LocationType, loc.ParentId,
                loc.Barcode, loc.Description, loc.SortOrder, loc.IsActive,
                path, contentCount, children);
        }).ToList();
    }

    public async Task<List<StorageLocationFlatResponseModel>> GetBinLocationsAsync(CancellationToken ct)
    {
        var locations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .ToListAsync(ct);

        var byId = locations.ToDictionary(l => l.Id);
        return locations
            .Where(l => l.LocationType == LocationType.Bin)
            .Select(l => new StorageLocationFlatResponseModel(
                l.Id, l.Name, l.LocationType, l.Barcode, BuildPath(l, byId)))
            .ToList();
    }

    public async Task<PagedResponse<StorageLocationFlatResponseModel>> GetBinLocationsPagedAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        const int defaultPageSize = 20;
        const int maxPageSize = 100;

        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize <= 0
            ? defaultPageSize
            : Math.Min(pageSize, maxPageSize);

        // Path is a composed string (parent name / child name / …) so we
        // materialize the bin set first then filter / page in memory. Bin
        // counts are bounded enough for this to be cheap.
        var allLocations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .ToListAsync(ct);

        var byId = allLocations.ToDictionary(l => l.Id);

        var bins = allLocations
            .Where(l => l.LocationType == LocationType.Bin)
            .Select(l => new StorageLocationFlatResponseModel(
                l.Id, l.Name, l.LocationType, l.Barcode, BuildPath(l, byId)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bins = bins
                .Where(b =>
                    b.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (b.Barcode != null && b.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase))
                    || b.LocationPath.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var totalCount = bins.Count;
        var paged = bins
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToList();

        return new PagedResponse<StorageLocationFlatResponseModel>(
            paged, totalCount, effectivePage, effectivePageSize);
    }

    private static string BuildPath(StorageLocation loc, Dictionary<int, StorageLocation> byId)
    {
        var parts = new List<string> { loc.Name };
        var current = loc;
        while (current.ParentId.HasValue && byId.TryGetValue(current.ParentId.Value, out var parent))
        {
            parts.Insert(0, parent.Name);
            current = parent;
        }
        return string.Join(" / ", parts);
    }

    public Task<StorageLocation?> FindLocationAsync(int id, CancellationToken ct)
        => db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id && l.DeletedAt == null, ct);

    public Task<bool> BarcodeExistsAsync(string barcode, int? excludeId, CancellationToken ct)
    {
        var query = db.StorageLocations.Where(l => l.Barcode == barcode && l.DeletedAt == null);
        if (excludeId.HasValue)
            query = query.Where(l => l.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task AddLocationAsync(StorageLocation location, CancellationToken ct)
    {
        await db.StorageLocations.AddAsync(location, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<BinContentResponseModel>> GetBinContentsAsync(int locationId, CancellationToken ct)
    {
        var contents = await db.BinContents
            .Include(c => c.Location)
            .Include(c => c.Job)
            .Where(c => c.LocationId == locationId && c.RemovedAt == null)
            .OrderBy(c => c.EntityType).ThenBy(c => c.PlacedAt)
            .ToListAsync(ct);

        var allLocations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .ToListAsync(ct);
        var byId = allLocations.ToDictionary(l => l.Id);

        var partIds = contents.Where(c => c.EntityType == "part").Select(c => c.EntityId).Distinct().ToList();
        var parts = await db.Parts.Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return contents.Select(c => new BinContentResponseModel(
            c.Id, c.LocationId, c.Location.Name,
            BuildPath(c.Location, byId),
            c.EntityType, c.EntityId,
            c.EntityType == "part" && parts.TryGetValue(c.EntityId, out var part) ? $"{part.PartNumber} — {part.Description}" : $"{c.EntityType}:{c.EntityId}",
            c.Quantity, c.LotNumber, c.JobId,
            c.Job?.JobNumber, c.Status, c.PlacedAt
        )).ToList();
    }

    public Task<BinContent?> FindBinContentAsync(int id, CancellationToken ct)
        => db.BinContents.Include(c => c.Location).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddBinContentAsync(BinContent content, CancellationToken ct)
    {
        await db.BinContents.AddAsync(content, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMovementAsync(BinMovement movement, CancellationToken ct)
    {
        await db.BinMovements.AddAsync(movement, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<InventoryPartSummaryResponseModel>> GetPartInventorySummaryAsync(string? search, CancellationToken ct)
    {
        var query = db.Parts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.PartNumber.ToLower().Contains(term) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        var partsWithStock = await query
            .OrderBy(p => p.PartNumber)
            .ToListAsync(ct);

        var partIds = partsWithStock.Select(p => p.Id).ToList();

        var contents = await db.BinContents
            .Include(c => c.Location)
            .Where(c => c.EntityType == "part" && partIds.Contains(c.EntityId) && c.RemovedAt == null)
            .ToListAsync(ct);

        var allLocations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .ToListAsync(ct);
        var locById = allLocations.ToDictionary(l => l.Id);

        // Load lot records for any bin content that has a lot number
        var lotNumbers = contents
            .Where(c => c.LotNumber != null)
            .Select(c => c.LotNumber!)
            .Distinct()
            .ToList();

        var lots = lotNumbers.Any()
            ? await db.LotRecords
                .Where(l => lotNumbers.Contains(l.LotNumber) && l.DeletedAt == null)
                .ToListAsync(ct)
            : [];

        var lotByNumberAndPart = lots.ToDictionary(l => (l.LotNumber, l.PartId));

        var contentsByPart = contents.ToLookup(c => c.EntityId);

        return partsWithStock
            .Where(p => contentsByPart[p.Id].Any())
            .Select(p =>
            {
                var bins = contentsByPart[p.Id].ToList();
                var onHand = bins.Sum(b => b.Quantity);
                var reserved = bins.Sum(b => b.ReservedQuantity);

                return new InventoryPartSummaryResponseModel(
                    p.Id, p.PartNumber, p.Description ?? p.Name,
                    onHand, reserved, onHand - reserved,
                    bins.Select(b =>
                    {
                        var lot = b.LotNumber != null && lotByNumberAndPart.TryGetValue((b.LotNumber, p.Id), out var l) ? l : null;
                        return new BinStockResponseModel(
                            b.LocationId, b.Location.Name,
                            BuildPath(b.Location, locById),
                            b.Quantity, b.ReservedQuantity, b.Quantity - b.ReservedQuantity,
                            b.Status, b.LotNumber,
                            lot?.Id, lot?.ExpirationDate, lot?.SupplierLotNumber);
                    }).ToList());
            }).ToList();
    }

    public async Task<List<BinMovementResponseModel>> GetMovementsAsync(int? locationId, string? entityType, int? entityId, int take, CancellationToken ct)
    {
        var query = db.BinMovements
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(m => m.FromLocationId == locationId || m.ToLocationId == locationId);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(m => m.EntityType == entityType);

        if (entityId.HasValue)
            query = query.Where(m => m.EntityId == entityId.Value);

        var movements = await query
            .OrderByDescending(m => m.MovedAt)
            .Take(take)
            .ToListAsync(ct);

        var userIds = movements.Select(m => m.MovedBy).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var partIds = movements.Where(m => m.EntityType == "part").Select(m => m.EntityId).Distinct().ToList();
        var parts = await db.Parts.Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return movements.Select(m =>
        {
            var entityName = m.EntityType == "part" && parts.TryGetValue(m.EntityId, out var part)
                ? $"{part.PartNumber} — {part.Description}"
                : $"{m.EntityType}:{m.EntityId}";

            var userName = users.TryGetValue(m.MovedBy, out var user)
                ? $"{user.FirstName} {user.LastName}"
                : "Unknown";

            return new BinMovementResponseModel(
                m.Id, m.EntityType, m.EntityId, entityName,
                m.Quantity, m.LotNumber,
                m.FromLocationId, m.FromLocation?.Name,
                m.ToLocationId, m.ToLocation?.Name,
                userName, m.MovedAt, m.Reason);
        }).ToList();
    }

    public async Task<List<ReceivingRecordResponseModel>> GetReceivingHistoryAsync(
        int? purchaseOrderId, int? partId, int take, CancellationToken ct)
    {
        var query = db.ReceivingRecords
            .Include(r => r.PurchaseOrderLine)
                .ThenInclude(l => l.PurchaseOrder)
            .Include(r => r.PurchaseOrderLine)
                .ThenInclude(l => l.Part)
            .Include(r => r.StorageLocation)
            .AsQueryable();

        if (purchaseOrderId.HasValue)
            query = query.Where(r => r.PurchaseOrderLine.PurchaseOrderId == purchaseOrderId.Value);

        if (partId.HasValue)
            query = query.Where(r => r.PurchaseOrderLine.PartId == partId.Value);

        var records = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return records.Select(r => new ReceivingRecordResponseModel(
            r.Id,
            r.PurchaseOrderLineId,
            r.PurchaseOrderLine.PurchaseOrder.PONumber,
            r.PurchaseOrderLine.PartId,
            r.PurchaseOrderLine.Part.PartNumber,
            r.QuantityReceived,
            r.ReceivedBy,
            r.StorageLocationId,
            r.StorageLocation?.Name,
            null,
            r.Notes,
            r.CreatedAt
        )).ToList();
    }

    public Task<BinContent?> FindBinContentWithLocationAsync(int id, CancellationToken ct)
        => db.BinContents.Include(c => c.Location).FirstOrDefaultAsync(c => c.Id == id && c.RemovedAt == null, ct);

    public async Task<CycleCount?> FindCycleCountAsync(int id, CancellationToken ct)
        => await db.CycleCounts
            .Include(c => c.Location)
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<List<CycleCountResponseModel>> GetCycleCountsAsync(
        int? locationId, string? status, CancellationToken ct)
    {
        var query = db.CycleCounts
            .Include(c => c.Location)
            .Include(c => c.Lines)
            .AsQueryable();

        if (locationId.HasValue)
            query = query.Where(c => c.LocationId == locationId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(c => c.Status == status);

        var counts = await query
            .OrderByDescending(c => c.CountedAt)
            .ToListAsync(ct);

        var userIds = counts.Select(c => c.CountedById).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        var partIds = counts.SelectMany(c => c.Lines)
            .Where(l => l.EntityType == "part")
            .Select(l => l.EntityId)
            .Distinct()
            .ToList();
        var parts = await db.Parts.Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        return counts.Select(c => new CycleCountResponseModel(
            c.Id,
            c.LocationId,
            c.Location.Name,
            c.CountedById,
            users.TryGetValue(c.CountedById, out var user) ? $"{user.FirstName} {user.LastName}" : "Unknown",
            c.CountedAt,
            c.Status,
            c.Notes,
            c.Lines.Select(l => new CycleCountLineResponseModel(
                l.Id,
                l.BinContentId,
                l.EntityType,
                l.EntityId,
                l.EntityType == "part" && parts.TryGetValue(l.EntityId, out var part)
                    ? $"{part.PartNumber} — {part.Description}"
                    : $"{l.EntityType}:{l.EntityId}",
                l.ExpectedQuantity,
                l.ActualQuantity,
                l.Variance,
                l.Notes
            )).ToList(),
            c.CreatedAt
        )).ToList();
    }

    public async Task AddCycleCountAsync(CycleCount cycleCount, CancellationToken ct)
    {
        await db.CycleCounts.AddAsync(cycleCount, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ReservationResponseModel>> GetReservationsAsync(int? partId, int? jobId, CancellationToken ct)
    {
        var query = db.Reservations
            .Include(r => r.Part)
            .Include(r => r.BinContent)
                .ThenInclude(b => b.Location)
            .Include(r => r.Job)
            .Where(r => r.DeletedAt == null)
            .AsQueryable();

        if (partId.HasValue)
            query = query.Where(r => r.PartId == partId.Value);

        if (jobId.HasValue)
            query = query.Where(r => r.JobId == jobId.Value);

        var reservations = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var allLocations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .ToListAsync(ct);
        var locById = allLocations.ToDictionary(l => l.Id);

        return reservations.Select(r => new ReservationResponseModel(
            r.Id,
            r.PartId,
            r.Part.PartNumber,
            r.Part.Description ?? r.Part.Name,
            r.BinContentId,
            BuildPath(r.BinContent.Location, locById),
            r.JobId,
            r.Job?.Title,
            r.Job?.JobNumber,
            r.SalesOrderLineId,
            r.Quantity,
            r.Notes,
            r.CreatedAt
        )).ToList();
    }

    public Task<Reservation?> FindReservationAsync(int id, CancellationToken ct)
        => db.Reservations
            .Include(r => r.BinContent)
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, ct);

    public async Task AddReservationAsync(Reservation reservation, CancellationToken ct)
    {
        await db.Reservations.AddAsync(reservation, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}
