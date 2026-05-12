using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IJobRepository
{
    /// <summary>
    /// Legacy (non-paged) list. Kept for any internal caller that still needs
    /// the full flat array (kanban / calendar paths). New work should call
    /// <see cref="GetPagedJobsAsync"/>.
    /// </summary>
    Task<List<JobListResponseModel>> GetJobsAsync(int? trackTypeId, int? stageId, int? assigneeId, bool isArchived, string? search, CancellationToken ct, int? customerId = null);

    /// <summary>
    /// Paged list per the Phase 3 F7-broad / WU-22 standard contract. Returns
    /// the slice + the total matching count for pagination UI. Specialised
    /// list endpoints (kanban, calendar) remain on the legacy unpaged path.
    /// </summary>
    Task<PagedResponse<JobListResponseModel>> GetPagedJobsAsync(JobListQuery query, CancellationToken ct);

    Task<JobDetailResponseModel?> GetDetailAsync(int id, CancellationToken ct);
    Task<Job?> FindAsync(int id, CancellationToken ct);
    Task<string> GenerateNextJobNumberAsync(CancellationToken ct);
    Task<int> GetMaxBoardPositionAsync(int stageId, CancellationToken ct);
    Task<List<Job>> FindMultipleAsync(List<int> ids, CancellationToken ct);
    Task<List<ChildJobResponseModel>> GetChildJobsAsync(int parentJobId, CancellationToken ct);
    Task AddAsync(Job job, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
