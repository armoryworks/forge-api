using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IActivityLogRepository
{
    Task<List<ActivityResponseModel>> GetByJobIdAsync(int jobId, CancellationToken ct);
    Task<List<ActivityResponseModel>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct);
    Task<bool> JobExistsAsync(int jobId, CancellationToken ct);
    Task AddAsync(JobActivityLog log, CancellationToken ct);
    Task AddAsync(ActivityLog log, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
