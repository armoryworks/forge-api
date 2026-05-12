using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class CustomerRepository(AppDbContext db) : ICustomerRepository
{
    public async Task<List<CustomerResponseModel>> GetAllActiveAsync(CancellationToken ct)
    {
        return await db.Customers
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CustomerResponseModel(c.Id, c.Name))
            .ToListAsync(ct);
    }

    public async Task<List<CustomerListItemModel>> GetAllAsync(string? search, bool? isActive, CancellationToken ct)
    {
        // Legacy non-paged path. Routes to the paged implementation under the
        // hood with a wide page so existing internal callers (which expected the
        // full flat list) are not affected. The 200 cap is the contract upper
        // bound; if a tenant has > 200 customers and a caller still uses this
        // path, they get the first 200 — that's an explicit migration nudge to
        // GetPagedAsync. (Phase 3 F7-partial / WU-17.)
        var paged = await GetPagedAsync(new CustomerListQuery
        {
            Q = search,
            IsActive = isActive,
            PageSize = 200,
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<CustomerListItemModel>> GetPagedAsync(
        CustomerListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-partial / WU-17 — standardised paged-list contract.
        // Sort is whitelisted to a fixed set of safe columns to prevent EF
        // injection; anything outside the whitelist falls back to the default
        // (createdAt desc). Stable secondary sort by Id keeps page boundaries
        // deterministic when the primary sort key has duplicates.
        var q = db.Customers.AsQueryable();

        // — Filters —
        if (query.IsActive.HasValue)
            q = q.Where(c => c.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.CompanyName != null && c.CompanyName.ToLower().Contains(term)) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.DefaultCurrency))
        {
            var cur = query.DefaultCurrency.Trim().ToUpper();
            q = q.Where(c => c.DefaultCurrency != null && c.DefaultCurrency.ToUpper() == cur);
        }

        if (query.DateFrom.HasValue)
            q = q.Where(c => c.CreatedAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(c => c.CreatedAt <= query.DateTo.Value);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc, stable secondary by Id) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        // The WU-17 charter says "default to CreatedAt DESC" — preserve that
        // when no sort is provided, regardless of the order param.
        var explicitSort = !string.IsNullOrEmpty(sortKey);
        IOrderedQueryable<Customer> ordered = sortKey switch
        {
            "name"        => desc ? q.OrderByDescending(c => c.Name)        : q.OrderBy(c => c.Name),
            "companyname" => desc ? q.OrderByDescending(c => c.CompanyName) : q.OrderBy(c => c.CompanyName),
            "email"       => desc ? q.OrderByDescending(c => c.Email)       : q.OrderBy(c => c.Email),
            "phone"       => desc ? q.OrderByDescending(c => c.Phone)       : q.OrderBy(c => c.Phone),
            "isactive"    => desc ? q.OrderByDescending(c => c.IsActive)    : q.OrderBy(c => c.IsActive),
            "createdat"   => desc ? q.OrderByDescending(c => c.CreatedAt)   : q.OrderBy(c => c.CreatedAt),
            "updatedat"   => desc ? q.OrderByDescending(c => c.UpdatedAt)   : q.OrderBy(c => c.UpdatedAt),
            "id"          => desc ? q.OrderByDescending(c => c.Id)          : q.OrderBy(c => c.Id),
            _ => q.OrderByDescending(c => c.CreatedAt),
        };
        // Stable secondary sort by id so page boundaries don't shuffle when
        // the primary key has ties (e.g. several customers created the same
        // second during seed).
        ordered = ordered.ThenBy(c => c.Id);

        // — Page slice + projection —
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(c => new CustomerListItemModel(
                c.Id,
                c.Name,
                c.CompanyName,
                c.Email,
                c.Phone,
                c.IsActive,
                c.Contacts.Count(ct => ct.DeletedAt == null),
                c.Jobs.Count(j => j.DeletedAt == null),
                c.CreatedAt))
            .ToListAsync(ct);

        // explicitSort is unused at runtime but kept here to make the
        // "default sort = createdAt desc regardless of order" intent explicit.
        _ = explicitSort;

        return new PagedResponse<CustomerListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<Customer?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Customer?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        // Phase 3 F3 — also pull addresses so the detail GET surfaces the
        // billing/shipping data the create-with-full-record path persisted.
        return await db.Customers
            .Include(c => c.Contacts.Where(ct => ct.DeletedAt == null))
            .Include(c => c.Jobs.Where(j => j.DeletedAt == null))
                .ThenInclude(j => j.CurrentStage)
            .Include(c => c.Addresses.Where(a => a.DeletedAt == null))
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task AddAsync(Customer customer, CancellationToken ct)
    {
        await db.Customers.AddAsync(customer, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
