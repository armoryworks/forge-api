using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class VendorRepository(AppDbContext db) : IVendorRepository
{
    public async Task<List<VendorResponseModel>> GetAllActiveAsync(CancellationToken ct)
    {
        // Phase 3 H2 / WU-12: dropdown returns ALL vendors (active first,
        // then inactive) so the UI can render inactive entries greyed-out
        // with a "(deactivated)" suffix. The UI defaults to filtering
        // active-only and exposes a "Show inactive" toggle. The new
        // IsActive flag in the projection drives that.
        return await db.Vendors
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.CompanyName)
            .Select(v => new VendorResponseModel(v.Id, v.CompanyName, v.IsActive))
            .ToListAsync(ct);
    }

    public async Task<List<VendorListItemModel>> GetAllAsync(string? search, bool? isActive, CancellationToken ct)
    {
        // Legacy non-paged path. Routes to the paged implementation under the
        // hood with a wide page so existing internal callers (which expected
        // the full flat list) are not affected. The 200 cap is the contract
        // upper bound; if a tenant has > 200 vendors and a caller still uses
        // this path, they get the first 200 — that's an explicit migration
        // nudge to GetPagedAsync. (Phase 3 F7-broad / WU-22.)
        var paged = await GetPagedAsync(new VendorListQuery
        {
            Q = search,
            IsActive = isActive,
            PageSize = 200,
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<VendorListItemModel>> GetPagedAsync(
        VendorListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-broad / WU-22 — standardised paged-list contract.
        // Sort is whitelisted to a fixed set of safe columns to prevent EF
        // injection; anything outside the whitelist falls back to the default
        // (createdAt desc). Stable secondary sort by Id keeps page boundaries
        // deterministic when the primary sort key has duplicates.
        var q = db.Vendors.AsQueryable();

        // — Filters —
        if (query.IsActive.HasValue)
            q = q.Where(v => v.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(v =>
                v.CompanyName.ToLower().Contains(term) ||
                (v.ContactName != null && v.ContactName.ToLower().Contains(term)) ||
                (v.Email != null && v.Email.ToLower().Contains(term)) ||
                (v.Phone != null && v.Phone.ToLower().Contains(term)));
        }

        if (query.DateFrom.HasValue)
            q = q.Where(v => v.CreatedAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(v => v.CreatedAt <= query.DateTo.Value);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc, stable secondary by Id) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Vendor> ordered = sortKey switch
        {
            "name"        => desc ? q.OrderByDescending(v => v.CompanyName) : q.OrderBy(v => v.CompanyName),
            "companyname" => desc ? q.OrderByDescending(v => v.CompanyName) : q.OrderBy(v => v.CompanyName),
            "contactname" => desc ? q.OrderByDescending(v => v.ContactName) : q.OrderBy(v => v.ContactName),
            "email"       => desc ? q.OrderByDescending(v => v.Email)       : q.OrderBy(v => v.Email),
            "phone"       => desc ? q.OrderByDescending(v => v.Phone)       : q.OrderBy(v => v.Phone),
            "isactive"    => desc ? q.OrderByDescending(v => v.IsActive)    : q.OrderBy(v => v.IsActive),
            "createdat"   => desc ? q.OrderByDescending(v => v.CreatedAt)   : q.OrderBy(v => v.CreatedAt),
            "updatedat"   => desc ? q.OrderByDescending(v => v.UpdatedAt)   : q.OrderBy(v => v.UpdatedAt),
            "id"          => desc ? q.OrderByDescending(v => v.Id)          : q.OrderBy(v => v.Id),
            _ => q.OrderByDescending(v => v.CreatedAt),
        };
        ordered = ordered.ThenBy(v => v.Id);

        // — Page slice + projection —
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(v => new VendorListItemModel(
                v.Id,
                v.CompanyName,
                v.ContactName,
                v.Email,
                v.Phone,
                v.IsActive,
                v.PurchaseOrders.Count(po => po.DeletedAt == null),
                v.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<VendorListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<Vendor?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<Vendor?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.Vendors
            .Include(v => v.PurchaseOrders.Where(po => po.DeletedAt == null))
                .ThenInclude(po => po.Job)
            .Include(v => v.PurchaseOrders.Where(po => po.DeletedAt == null))
                .ThenInclude(po => po.Lines)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task AddAsync(Vendor vendor, CancellationToken ct)
    {
        await db.Vendors.AddAsync(vendor, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
