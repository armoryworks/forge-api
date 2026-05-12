using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class PriceListRepository(AppDbContext db) : IPriceListRepository
{
    public async Task<List<PriceListListItemModel>> GetAllAsync(int? customerId, CancellationToken ct)
    {
        var query = db.PriceLists
            .Include(pl => pl.Customer)
            .Include(pl => pl.Entries)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(pl => pl.CustomerId == customerId.Value);

        return await query
            .OrderByDescending(pl => pl.IsDefault)
            .ThenBy(pl => pl.Name)
            .Select(pl => new PriceListListItemModel(
                pl.Id,
                pl.Name,
                pl.Description,
                pl.CustomerId,
                pl.Customer != null ? pl.Customer.Name : null,
                pl.IsDefault,
                pl.IsActive,
                pl.Entries.Count,
                pl.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PriceList?> FindAsync(int id, CancellationToken ct)
    {
        return await db.PriceLists.FirstOrDefaultAsync(pl => pl.Id == id, ct);
    }

    public async Task<PriceList?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.PriceLists
            .Include(pl => pl.Customer)
            .Include(pl => pl.Entries)
                .ThenInclude(e => e.Part)
            .FirstOrDefaultAsync(pl => pl.Id == id, ct);
    }

    public async Task AddAsync(PriceList priceList, CancellationToken ct)
    {
        await db.PriceLists.AddAsync(priceList, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsetDefaultForScopeAsync(int? customerId, int? excludePriceListId, CancellationToken ct)
    {
        // Scope check: customerId-bounded vs. system-wide. Equality on a
        // nullable int doesn't translate cleanly across providers, so split
        // the predicate explicitly.
        var query = customerId.HasValue
            ? db.PriceLists.Where(pl => pl.CustomerId == customerId.Value)
            : db.PriceLists.Where(pl => pl.CustomerId == null);

        if (excludePriceListId.HasValue)
            query = query.Where(pl => pl.Id != excludePriceListId.Value);

        var defaults = await query.Where(pl => pl.IsDefault).ToListAsync(ct);
        foreach (var pl in defaults) pl.IsDefault = false;
    }

    public async Task<bool> PriceListExistsAsync(int priceListId, CancellationToken ct)
    {
        return await db.PriceLists.AnyAsync(pl => pl.Id == priceListId, ct);
    }

    public async Task<PagedResponse<PriceListEntryResponseModel>> GetEntriesAsync(
        int priceListId, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.PriceListEntries
            .AsNoTracking()
            .Include(e => e.Part)
            .Where(e => e.PriceListId == priceListId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.Part.PartNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Part.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = await query.CountAsync(ct);

        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 50 : (pageSize > 200 ? 200 : pageSize);

        var items = await query
            .OrderBy(e => e.Part.PartNumber)
            .ThenBy(e => e.MinQuantity)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(e => new PriceListEntryResponseModel(
                e.Id, e.PriceListId, e.PartId, e.Part.PartNumber, e.Part.Name,
                e.UnitPrice, e.MinQuantity, e.Currency, e.Notes,
                e.CreatedAt, e.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<PriceListEntryResponseModel>(items, totalCount, effectivePage, effectivePageSize);
    }

    public async Task<PriceListEntry?> FindEntryAsync(int entryId, CancellationToken ct)
    {
        return await db.PriceListEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
    }

    public async Task<PriceListEntry?> FindEntryWithPartAsync(int entryId, CancellationToken ct)
    {
        return await db.PriceListEntries
            .Include(e => e.Part)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);
    }

    public async Task AddEntryAsync(PriceListEntry entry, CancellationToken ct)
    {
        await db.PriceListEntries.AddAsync(entry, ct);
    }
}
