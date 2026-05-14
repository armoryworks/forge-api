using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class PlanningCycleRepository(AppDbContext db) : IPlanningCycleRepository
{
    public async Task<List<PlanningCycleListItemModel>> GetAllAsync(CancellationToken ct)
    {
        return await db.PlanningCycles
            .OrderByDescending(c => c.StartDate)
            .Select(c => new PlanningCycleListItemModel(
                c.Id,
                c.Name,
                c.StartDate,
                c.EndDate,
                c.Status.ToString(),
                c.Entries.Count,
                c.Entries.Count(e => e.CompletedAt.HasValue),
                c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PlanningCycle?> FindAsync(int id, CancellationToken ct)
    {
        return await db.PlanningCycles.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<PlanningCycle?> FindWithDetailsAsync(int id, CancellationToken ct)
    {
        return await db.PlanningCycles
            .Include(c => c.Entries.OrderBy(e => e.SortOrder))
                .ThenInclude(e => e.Job)
                    .ThenInclude(j => j.CurrentStage)
            .Include(c => c.Entries)
                .ThenInclude(e => e.Job)
                    .ThenInclude(j => j.TrackType)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<PlanningCycle?> GetCurrentAsync(CancellationToken ct)
    {
        return await db.PlanningCycles
            .Include(c => c.Entries.OrderBy(e => e.SortOrder))
                .ThenInclude(e => e.Job)
                    .ThenInclude(j => j.CurrentStage)
            .Include(c => c.Entries)
                .ThenInclude(e => e.Job)
                    .ThenInclude(j => j.TrackType)
            .Where(c => c.Status != PlanningCycleStatus.Completed)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PlanningCycleEntry?> FindEntryAsync(int cycleId, int jobId, CancellationToken ct)
    {
        return await db.PlanningCycleEntries
            .FirstOrDefaultAsync(e => e.PlanningCycleId == cycleId && e.JobId == jobId, ct);
    }

    public async Task AddAsync(PlanningCycle cycle, CancellationToken ct)
    {
        await db.PlanningCycles.AddAsync(cycle, ct);
    }

    public async Task AddEntryAsync(PlanningCycleEntry entry, CancellationToken ct)
    {
        await db.PlanningCycleEntries.AddAsync(entry, ct);
    }

    public void RemoveEntry(PlanningCycleEntry entry)
    {
        db.PlanningCycleEntries.Remove(entry);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
