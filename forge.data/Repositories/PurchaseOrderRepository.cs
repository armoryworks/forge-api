using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class PurchaseOrderRepository(AppDbContext db) : IPurchaseOrderRepository
{
    public async Task<List<PurchaseOrderListItemModel>> GetAllAsync(
        int? vendorId, int? jobId, PurchaseOrderStatus? status, CancellationToken ct)
    {
        // Legacy non-paged path. Routes to the paged implementation under the
        // hood with a wide page so existing internal callers (which expected
        // the full flat list) are not affected. (Phase 3 F7-broad / WU-22.)
        var paged = await GetPagedAsync(new PurchaseOrderListQuery
        {
            VendorId = vendorId,
            JobId = jobId,
            Status = status,
            PageSize = 200,
        }, ct);
        return paged.Items.ToList();
    }

    public async Task<PagedResponse<PurchaseOrderListItemModel>> GetPagedAsync(
        PurchaseOrderListQuery query, CancellationToken ct)
    {
        // Phase 3 F7-broad / WU-22 — standardised paged-list contract.
        // Sort is whitelisted to a fixed set of safe columns to prevent EF
        // injection. Stable secondary sort by Id keeps page boundaries
        // deterministic.
        var q = db.PurchaseOrders
            .Include(po => po.Vendor)
            .Include(po => po.Job)
            .Include(po => po.Lines)
            .AsQueryable();

        // — Filters —
        if (query.VendorId.HasValue)
            q = q.Where(po => po.VendorId == query.VendorId.Value);

        if (query.JobId.HasValue)
            q = q.Where(po => po.JobId == query.JobId.Value);

        if (query.Status.HasValue)
            q = q.Where(po => po.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(po =>
                po.PONumber.ToLower().Contains(term) ||
                po.Vendor.CompanyName.ToLower().Contains(term) ||
                (po.Job != null && po.Job.JobNumber.ToLower().Contains(term)));
        }

        // PO doesn't have a separate OrderDate — CreatedAt is the canonical
        // order date for filtering purposes.
        if (query.DateFrom.HasValue)
            q = q.Where(po => po.CreatedAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(po => po.CreatedAt <= query.DateTo.Value);

        // Filter out POs whose vendor was soft-deleted — the projection below
        // inner-joins Vendor via po.Vendor.CompanyName, so without this the
        // count drifts ahead of len(items) and pages can return short of
        // pageSize even when more rows exist. (Phase 3 F7-broad / WU-22.)
        q = q.Where(po => po.Vendor != null);

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<PurchaseOrder> ordered = sortKey switch
        {
            "ponumber"            => desc ? q.OrderByDescending(po => po.PONumber)              : q.OrderBy(po => po.PONumber),
            "vendor"              => desc ? q.OrderByDescending(po => po.Vendor.CompanyName)    : q.OrderBy(po => po.Vendor.CompanyName),
            "vendorname"          => desc ? q.OrderByDescending(po => po.Vendor.CompanyName)    : q.OrderBy(po => po.Vendor.CompanyName),
            "status"              => desc ? q.OrderByDescending(po => po.Status)                : q.OrderBy(po => po.Status),
            "expecteddeliverydate"=> desc ? q.OrderByDescending(po => po.ExpectedDeliveryDate)  : q.OrderBy(po => po.ExpectedDeliveryDate),
            "createdat"           => desc ? q.OrderByDescending(po => po.CreatedAt)             : q.OrderBy(po => po.CreatedAt),
            "orderdate"           => desc ? q.OrderByDescending(po => po.CreatedAt)             : q.OrderBy(po => po.CreatedAt),
            "updatedat"           => desc ? q.OrderByDescending(po => po.UpdatedAt)             : q.OrderBy(po => po.UpdatedAt),
            "id"                  => desc ? q.OrderByDescending(po => po.Id)                    : q.OrderBy(po => po.Id),
            _ => q.OrderByDescending(po => po.CreatedAt),
        };
        ordered = ordered.ThenBy(po => po.Id);

        // — Page slice + projection —
        // Bought-parts effort PR2.5 — BelowVendorMinimum is computed in the
        // projection (line value + EstimatedFreight vs Vendor.MinOrderAmount)
        // so the list can render a warning chip without a second round trip.
        var items = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Select(po => new PurchaseOrderListItemModel(
                po.Id,
                po.PONumber,
                po.VendorId,
                po.Vendor.CompanyName,
                po.JobId,
                po.Job != null ? po.Job.JobNumber : null,
                po.Status.ToString(),
                po.Lines.Count,
                po.Lines.Sum(l => l.OrderedQuantity),
                po.Lines.Sum(l => l.ReceivedQuantity),
                po.ExpectedDeliveryDate,
                po.IsBlanket,
                po.CreatedAt,
                po.Vendor.MinOrderAmount.HasValue
                    && po.Vendor.MinOrderAmount.Value > 0m
                    && po.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice)
                        + (po.EstimatedFreight ?? 0m)
                        < po.Vendor.MinOrderAmount.Value))
            .ToListAsync(ct);

        return new PagedResponse<PurchaseOrderListItemModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    public async Task<PurchaseOrder?> FindAsync(int id, CancellationToken ct)
    {
        return await db.PurchaseOrders.FirstOrDefaultAsync(po => po.Id == id, ct);
    }

    public async Task<PurchaseOrder?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.PurchaseOrders
            .Include(po => po.Vendor)
            .Include(po => po.Job)
            .Include(po => po.Lines)
                .ThenInclude(l => l.Part)
            .Include(po => po.Lines)
                .ThenInclude(l => l.ReceivingRecords.Where(r => r.DeletedAt == null))
            .FirstOrDefaultAsync(po => po.Id == id, ct);
    }

    public async Task<PurchaseOrderLine?> FindLineAsync(int lineId, CancellationToken ct)
    {
        return await db.PurchaseOrderLines
            .Include(l => l.PurchaseOrder)
            .Include(l => l.Part)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
    }

    public async Task<string> GenerateNextPONumberAsync(CancellationToken ct)
    {
        var lastPo = await db.PurchaseOrders
            .IgnoreQueryFilters()
            .OrderByDescending(po => po.Id)
            .Select(po => po.PONumber)
            .FirstOrDefaultAsync(ct);

        if (lastPo != null && lastPo.StartsWith("PO-") && int.TryParse(lastPo[3..], out var lastNum))
            return $"PO-{lastNum + 1:D5}";

        return "PO-00001";
    }

    public async Task AddAsync(PurchaseOrder po, CancellationToken ct)
    {
        await db.PurchaseOrders.AddAsync(po, ct);
    }

    public async Task AddReceivingRecordAsync(ReceivingRecord record, CancellationToken ct)
    {
        await db.ReceivingRecords.AddAsync(record, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
