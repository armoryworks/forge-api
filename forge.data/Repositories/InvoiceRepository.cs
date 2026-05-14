using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class InvoiceRepository(AppDbContext db) : IInvoiceRepository
{
    public async Task<List<InvoiceListItemModel>> GetAllAsync(
        int? customerId, InvoiceStatus? status, CancellationToken ct)
    {
        // Legacy non-paged path. Routes to the paged implementation under the
        // hood with a wide page so existing internal callers (which expected
        // the full flat list) are not affected. (Phase 3 F7-broad / WU-22.)
        var paged = await GetPagedAsync(new InvoiceListQuery
        {
            CustomerId = customerId,
            Status = status,
            PageSize = 200,
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<InvoiceListItemModel>> GetPagedAsync(
        InvoiceListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-broad / WU-22 — standardised paged-list contract.
        // Sort is whitelisted; default = createdAt desc; stable secondary by Id.
        var q = db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .AsQueryable();

        // — Filters —
        if (query.CustomerId.HasValue)
            q = q.Where(i => i.CustomerId == query.CustomerId.Value);

        if (query.Status.HasValue)
            q = q.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(i =>
                i.InvoiceNumber.ToLower().Contains(term) ||
                i.Customer.Name.ToLower().Contains(term) ||
                (i.CustomerPO != null && i.CustomerPO.ToLower().Contains(term)));
        }

        // Date range filters apply to InvoiceDate (the canonical date).
        if (query.DateFrom.HasValue)
            q = q.Where(i => i.InvoiceDate >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(i => i.InvoiceDate <= query.DateTo.Value);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Invoice> ordered = sortKey switch
        {
            "invoicenumber"=> desc ? q.OrderByDescending(i => i.InvoiceNumber)    : q.OrderBy(i => i.InvoiceNumber),
            "customer"     => desc ? q.OrderByDescending(i => i.Customer.Name)    : q.OrderBy(i => i.Customer.Name),
            "customername" => desc ? q.OrderByDescending(i => i.Customer.Name)    : q.OrderBy(i => i.Customer.Name),
            "status"       => desc ? q.OrderByDescending(i => i.Status)           : q.OrderBy(i => i.Status),
            "invoicedate"  => desc ? q.OrderByDescending(i => i.InvoiceDate)      : q.OrderBy(i => i.InvoiceDate),
            "duedate"      => desc ? q.OrderByDescending(i => i.DueDate)          : q.OrderBy(i => i.DueDate),
            "createdat"    => desc ? q.OrderByDescending(i => i.CreatedAt)        : q.OrderBy(i => i.CreatedAt),
            "updatedat"    => desc ? q.OrderByDescending(i => i.UpdatedAt)        : q.OrderBy(i => i.UpdatedAt),
            "id"           => desc ? q.OrderByDescending(i => i.Id)               : q.OrderBy(i => i.Id),
            _ => q.OrderByDescending(i => i.CreatedAt),
        };
        ordered = ordered.ThenBy(i => i.Id);

        // — Page slice + projection —
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(i => new InvoiceListItemModel(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Customer.Name,
                i.Status.ToString(),
                i.InvoiceDate,
                i.DueDate,
                i.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + i.TaxRate),
                i.PaymentApplications.Sum(pa => pa.Amount),
                i.Lines.Sum(l => l.Quantity * l.UnitPrice) * (1 + i.TaxRate) - i.PaymentApplications.Sum(pa => pa.Amount),
                i.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<InvoiceListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<Invoice?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<Invoice?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Shipment)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
                .ThenInclude(pa => pa.Payment)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct)
    {
        var last = await db.Invoices
            .IgnoreQueryFilters()
            .OrderByDescending(i => i.Id)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("INV-") && int.TryParse(last[4..], out var lastNum))
            return $"INV-{lastNum + 1:D5}";

        return "INV-00001";
    }

    public async Task AddAsync(Invoice invoice, CancellationToken ct)
    {
        await db.Invoices.AddAsync(invoice, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
