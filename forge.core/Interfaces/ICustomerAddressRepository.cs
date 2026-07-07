using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ICustomerAddressRepository
{
    Task<List<CustomerAddressResponseModel>> GetByCustomerAsync(int customerId, CancellationToken ct, bool includeInactive = false);
    Task<CustomerAddress?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(CustomerAddress address, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
