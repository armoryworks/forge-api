using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
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
                so.CreatedAt))
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

    public async Task<string> GenerateNextOrderNumberAsync(CancellationToken ct)
    {
        var last = await db.SalesOrders
            .IgnoreQueryFilters()
            .OrderByDescending(so => so.Id)
            .Select(so => so.OrderNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("SO-") && int.TryParse(last[3..], out var lastNum))
            return $"SO-{lastNum + 1:D5}";

        return "SO-00001";
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
