using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

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

        if (query.OpenOnly == true)
            q = q.Where(i =>
                i.Status != InvoiceStatus.Draft &&
                i.Status != InvoiceStatus.Voided &&
                i.Lines.Sum(l => l.Quantity * l.UnitPrice) + i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => l.Quantity * l.UnitPrice) * i.TaxRate - i.PaymentApplications.Sum(pa => pa.Amount) - i.PaymentApplications.Sum(pa => pa.DiscountAmount) > 0);

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
        var rows = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(i => new { Subtotal = i.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), Item = new InvoiceListItemModel(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Customer.Name,
                i.Status.ToString(),
                i.InvoiceDate,
                i.DueDate,
                i.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) + Math.Round(i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) * i.TaxRate, 2),
                i.PaymentApplications.Sum(pa => pa.Amount),
                i.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) + Math.Round(i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) * i.TaxRate, 2) - i.PaymentApplications.Sum(pa => pa.Amount) - i.PaymentApplications.Sum(pa => pa.DiscountAmount),
                i.CreatedAt,
                db.Currencies.Where(c => c.Id == i.CurrencyId).Select(c => c.Code).FirstOrDefault(),
                i.CurrencyId,
                i.FxRate,
                Math.Round((i.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) + Math.Round(i.Lines.Where(l => l.TaxCode != InvoiceLine.NonTaxableCode).Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)) * i.TaxRate, 2)) * i.FxRate, 2),
                i.PaymentTermsId,
                i.DiscountPercent,
                i.DiscountDueDate,
                null,
                i.PaymentApplications.Sum(pa => pa.DiscountAmount)) })
            .ToListAsync(ct);

        var discounts = await DiscountSettings.ReadAsync(db, ct);
        var items = rows.Select(r => r.Item with
        {
            DiscountDeadline = EarlyPaymentDiscount.Deadline(r.Item.DiscountDueDate, discounts.GraceDays),
            DiscountAvailable = r.Item.DiscountPercent > 0m && r.Item.BalanceDue > 0m
                ? Math.Max(0m, EarlyPaymentDiscount.Maximum(discounts.BaseIsSubtotal ? r.Subtotal : r.Item.Total, r.Item.DiscountPercent) - r.Item.DiscountsTaken)
                : 0m,
        }).ToList();

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
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Shipment)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
                .ThenInclude(pa => pa.Payment)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is not null)
            await db.Entry(invoice).Reference(i => i.Currency).LoadAsync(ct);
        return invoice;
    }

    public Task<string> GenerateNextInvoiceNumberAsync(CancellationToken ct)
        => DocumentNumberSequence.NextAsync(
            db.Invoices.IgnoreQueryFilters().Select(i => i.InvoiceNumber), "INV", ct);

    public Task<bool> InvoiceNumberExistsAsync(string number, int? excludeId, CancellationToken ct)
    {
        var query = db.Invoices.Where(i => i.InvoiceNumber == number);
        if (excludeId.HasValue)
            query = query.Where(i => i.Id != excludeId.Value);
        return query.AnyAsync(ct);
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
