using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISalesOrderRepository
{
    Task<List<SalesOrderListItemModel>> GetAllAsync(int? customerId, SalesOrderStatus? status, CancellationToken ct);
    Task<SalesOrder?> FindAsync(int id, CancellationToken ct);
    Task<SalesOrder?> FindWithDetailsAsync(int id, CancellationToken ct);
    /// <summary>Next number in the default "SO" series.</summary>
    Task<string> GenerateNextOrderNumberAsync(CancellationToken ct);

    /// <summary>
    /// Next number in the given prefix series. Channels may define their own
    /// prefix, and each series numbers independently — see the implementation
    /// for why a global scan would collide.
    /// </summary>
    Task<string> GenerateNextOrderNumberAsync(string prefix, CancellationToken ct);
    Task AddAsync(SalesOrder order, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
