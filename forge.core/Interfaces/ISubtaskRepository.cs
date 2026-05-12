using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISubtaskRepository
{
    Task<List<SubtaskResponseModel>> GetByJobIdAsync(int jobId, CancellationToken ct);
    Task<JobSubtask?> FindAsync(int subtaskId, int jobId, CancellationToken ct);
    Task<bool> JobExistsAsync(int jobId, CancellationToken ct);
    Task<int> GetMaxSortOrderAsync(int jobId, CancellationToken ct);
    Task AddAsync(JobSubtask subtask, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
