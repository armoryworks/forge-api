using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IPlanningCycleRepository
{
    Task<List<PlanningCycleListItemModel>> GetAllAsync(CancellationToken ct);
    Task<PlanningCycle?> FindAsync(int id, CancellationToken ct);
    Task<PlanningCycle?> FindWithDetailsAsync(int id, CancellationToken ct);
    Task<PlanningCycle?> GetCurrentAsync(CancellationToken ct);
    Task<PlanningCycleEntry?> FindEntryAsync(int cycleId, int jobId, CancellationToken ct);
    Task AddAsync(PlanningCycle cycle, CancellationToken ct);
    Task AddEntryAsync(PlanningCycleEntry entry, CancellationToken ct);
    void RemoveEntry(PlanningCycleEntry entry);
    Task SaveChangesAsync(CancellationToken ct);
}
