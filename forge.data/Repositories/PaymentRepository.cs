using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public async Task<List<PaymentListItemModel>> GetAllAsync(int? customerId, CancellationToken ct)
    {
        // Legacy non-paged path. Routes to the paged implementation under the
        // hood with a wide page so existing internal callers (which expected
        // the full flat list) are not affected. (Phase 3 F7-broad / WU-22.)
        var paged = await GetPagedAsync(new PaymentListQuery
        {
            CustomerId = customerId,
            PageSize = 200,
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<PaymentListItemModel>> GetPagedAsync(
        PaymentListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-broad / WU-22 — standardised paged-list contract.
        // Sort is whitelisted; default = paymentDate desc; stable secondary by Id.
        var q = db.Payments
            .Include(p => p.Customer)
            .Include(p => p.Applications)
            .AsQueryable();

        // — Filters —
        if (query.CustomerId.HasValue)
            q = q.Where(p => p.CustomerId == query.CustomerId.Value);

        if (query.PaymentMethod.HasValue)
            q = q.Where(p => p.Method == query.PaymentMethod.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(p =>
                p.PaymentNumber.ToLower().Contains(term) ||
                p.Customer.Name.ToLower().Contains(term) ||
                (p.ReferenceNumber != null && p.ReferenceNumber.ToLower().Contains(term)));
        }

        // Date range applies to PaymentDate (the canonical date).
        if (query.DateFrom.HasValue)
            q = q.Where(p => p.PaymentDate >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(p => p.PaymentDate <= query.DateTo.Value);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = paymentDate desc) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Payment> ordered = sortKey switch
        {
            "paymentnumber"=> desc ? q.OrderByDescending(p => p.PaymentNumber)    : q.OrderBy(p => p.PaymentNumber),
            "customer"     => desc ? q.OrderByDescending(p => p.Customer.Name)    : q.OrderBy(p => p.Customer.Name),
            "customername" => desc ? q.OrderByDescending(p => p.Customer.Name)    : q.OrderBy(p => p.Customer.Name),
            "method"       => desc ? q.OrderByDescending(p => p.Method)           : q.OrderBy(p => p.Method),
            "paymentmethod"=> desc ? q.OrderByDescending(p => p.Method)           : q.OrderBy(p => p.Method),
            "amount"       => desc ? q.OrderByDescending(p => p.Amount)           : q.OrderBy(p => p.Amount),
            "paymentdate"  => desc ? q.OrderByDescending(p => p.PaymentDate)      : q.OrderBy(p => p.PaymentDate),
            "createdat"    => desc ? q.OrderByDescending(p => p.CreatedAt)        : q.OrderBy(p => p.CreatedAt),
            "updatedat"    => desc ? q.OrderByDescending(p => p.UpdatedAt)        : q.OrderBy(p => p.UpdatedAt),
            "id"           => desc ? q.OrderByDescending(p => p.Id)               : q.OrderBy(p => p.Id),
            _ => q.OrderByDescending(p => p.PaymentDate),
        };
        ordered = ordered.ThenBy(p => p.Id);

        // — Page slice + projection —
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(p => new PaymentListItemModel(
                p.Id,
                p.PaymentNumber,
                p.CustomerId,
                p.Customer.Name,
                p.Method.ToString(),
                p.Amount,
                p.Applications.Sum(a => a.Amount),
                p.Amount - p.Applications.Sum(a => a.Amount),
                p.PaymentDate,
                p.ReferenceNumber,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<PaymentListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<Payment?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Payment?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.Payments
            .Include(p => p.Customer)
            .Include(p => p.Applications)
                .ThenInclude(a => a.Invoice)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<string> GenerateNextPaymentNumberAsync(CancellationToken ct)
    {
        var last = await db.Payments
            .IgnoreQueryFilters()
            .OrderByDescending(p => p.Id)
            .Select(p => p.PaymentNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("PMT-") && int.TryParse(last[4..], out var lastNum))
            return $"PMT-{lastNum + 1:D5}";

        return "PMT-00001";
    }

    public async Task AddAsync(Payment payment, CancellationToken ct)
    {
        await db.Payments.AddAsync(payment, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
