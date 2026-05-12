using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class RecurringOrderRepository(AppDbContext db) : IRecurringOrderRepository
{
    public async Task<List<RecurringOrderListItemModel>> GetAllAsync(
        int? customerId, bool? isActive, CancellationToken ct)
    {
        var query = db.RecurringOrders
            .Include(ro => ro.Customer)
            .Include(ro => ro.Lines)
            .AsQueryable();

        if (customerId.HasValue)
            query = query.Where(ro => ro.CustomerId == customerId.Value);

        if (isActive.HasValue)
            query = query.Where(ro => ro.IsActive == isActive.Value);

        return await query
            .OrderBy(ro => ro.NextGenerationDate)
            .Select(ro => new RecurringOrderListItemModel(
                ro.Id,
                ro.Name,
                ro.CustomerId,
                ro.Customer.Name,
                ro.IntervalDays,
                ro.NextGenerationDate,
                ro.LastGeneratedDate,
                ro.IsActive,
                ro.Lines.Count,
                ro.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<RecurringOrder?> FindAsync(int id, CancellationToken ct)
    {
        return await db.RecurringOrders.FirstOrDefaultAsync(ro => ro.Id == id, ct);
    }

    public async Task<RecurringOrder?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.RecurringOrders
            .Include(ro => ro.Customer)
            .Include(ro => ro.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(ro => ro.Id == id, ct);
    }

    public async Task AddAsync(RecurringOrder recurringOrder, CancellationToken ct)
    {
        await db.RecurringOrders.AddAsync(recurringOrder, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
