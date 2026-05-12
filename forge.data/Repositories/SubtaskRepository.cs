using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SubtaskRepository(AppDbContext db) : ISubtaskRepository
{
    public async Task<List<SubtaskResponseModel>> GetByJobIdAsync(int jobId, CancellationToken ct)
    {
        return await db.JobSubtasks
            .Where(s => s.JobId == jobId)
            .OrderBy(s => s.SortOrder)
            .Select(s => new SubtaskResponseModel(
                s.Id,
                s.JobId,
                s.Text,
                s.IsCompleted,
                s.AssigneeId,
                s.SortOrder,
                s.CompletedAt))
            .ToListAsync(ct);
    }

    public async Task<JobSubtask?> FindAsync(int subtaskId, int jobId, CancellationToken ct)
    {
        return await db.JobSubtasks
            .FirstOrDefaultAsync(s => s.Id == subtaskId && s.JobId == jobId, ct);
    }

    public async Task<bool> JobExistsAsync(int jobId, CancellationToken ct)
    {
        return await db.Jobs.AnyAsync(j => j.Id == jobId, ct);
    }

    public async Task<int> GetMaxSortOrderAsync(int jobId, CancellationToken ct)
    {
        return await db.JobSubtasks
            .Where(s => s.JobId == jobId)
            .MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;
    }

    public Task AddAsync(JobSubtask subtask, CancellationToken ct)
    {
        db.JobSubtasks.Add(subtask);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
