using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SalesOrderRepository(AppDbContext db) : ISalesOrderRepository
{
    public async Task<List<SalesOrderListItemModel>> GetAllAsync(
        int? customerId, SalesOrderStatus? status, CancellationToken ct)
    {
        var query = db.SalesOrders
            .Include(so => so.Customer)
            .Include(so => so.Lines)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(so => so.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(so => so.Status == status.Value);

        return await query
            .OrderByDescending(so => so.CreatedAt)
            .Select(so => new SalesOrderListItemModel(
                so.Id,
                so.OrderNumber,
                so.CustomerId,
                so.Customer.Name,
                so.Status.ToString(),
                so.CustomerPO,
                so.Lines.Count,
                so.Lines.Sum(l => l.Quantity * l.UnitPrice),
                so.RequestedDeliveryDate,
                so.CreatedAt,
                so.Id,
                null))
            .ToListAsync(ct);
    }

    public async Task<SalesOrder?> FindAsync(int id, CancellationToken ct)
    {
        return await db.SalesOrders.FirstOrDefaultAsync(so => so.Id == id, ct);
    }

    public async Task<SalesOrder?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.SalesOrders
            .Include(so => so.Customer)
            .Include(so => so.Quote)
            .Include(so => so.Lines)
                .ThenInclude(l => l.Part)
            .Include(so => so.Lines)
                .ThenInclude(l => l.ShipmentLines)
            .Include(so => so.Lines)
                .ThenInclude(l => l.Jobs)
                    .ThenInclude(j => j.CurrentStage)
            .Include(so => so.Shipments)
                .ThenInclude(s => s.Lines)
                    .ThenInclude(sl => sl.Part)
            .Include(so => so.Shipments)
                .ThenInclude(s => s.Packages)
            .FirstOrDefaultAsync(so => so.Id == id, ct);
    }

    public Task<string> GenerateNextOrderNumberAsync(CancellationToken ct)
        => GenerateNextOrderNumberAsync(DefaultOrderNumberPrefix, ct);

    /// <summary>
    /// Next order number within a prefix series.
    ///
    /// <para>Per-prefix rather than global. Channels may carry their own
    /// <c>OrderNumberPrefix</c> so marketplace orders are recognizable in a
    /// mixed list, and the sequences must not interfere: scanning globally for
    /// the newest row would find (say) <c>EB-00042</c> when generating an
    /// <c>SO-</c> number, fail the prefix match, and restart the SO series at
    /// 00001 — colliding with the unique index on the very next insert.</para>
    /// </summary>
    public async Task<string> GenerateNextOrderNumberAsync(string prefix, CancellationToken ct)
    {
        var token = $"{prefix}-";

        var last = await db.SalesOrders
            .IgnoreQueryFilters()
            .Where(so => so.OrderNumber.StartsWith(token))
            .OrderByDescending(so => so.Id)
            .Select(so => so.OrderNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && int.TryParse(last[token.Length..], out var lastNum))
            return $"{token}{lastNum + 1:D5}";

        return $"{token}00001";
    }

    private const string DefaultOrderNumberPrefix = "SO";

    public Task<bool> OrderNumberExistsAsync(string number, int? excludeId, CancellationToken ct)
    {
        var query = db.SalesOrders.Where(so => so.OrderNumber == number);
        if (excludeId.HasValue)
            query = query.Where(so => so.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task AddAsync(SalesOrder order, CancellationToken ct)
    {
        await db.SalesOrders.AddAsync(order, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
