using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IRecurringOrderRepository
{
    Task<List<RecurringOrderListItemModel>> GetAllAsync(int? customerId, bool? isActive, CancellationToken ct);
    Task<RecurringOrder?> FindAsync(int id, CancellationToken ct);
    Task<RecurringOrder?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task AddAsync(RecurringOrder recurringOrder, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
