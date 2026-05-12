using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IStatusEntryRepository
{
    Task<List<StatusEntryResponseModel>> GetHistoryAsync(string entityType, int entityId, CancellationToken ct);
    Task<StatusEntryResponseModel?> GetCurrentWorkflowStatusAsync(string entityType, int entityId, CancellationToken ct);
    Task<List<StatusEntryResponseModel>> GetActiveHoldsAsync(string entityType, int entityId, CancellationToken ct);
    Task<StatusEntry?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(StatusEntry entry, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
