using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class JobRepository(AppDbContext db, IClock clock) : IJobRepository
{
    public async Task<List<JobListResponseModel>> GetJobsAsync(
        int? trackTypeId, int? stageId, int? assigneeId,
        bool isArchived, string? search, CancellationToken ct, int? customerId = null)
    {
        // Legacy (unpaged) path — kanban + calendar load all jobs in stage
        // order. Paged callers should use GetPagedJobsAsync instead.
        // (Phase 3 F7-broad / WU-22.)
        var query = db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.Customer)
            .Where(j => j.IsArchived == isArchived);

        if (trackTypeId.HasValue)
            query = query.Where(j => j.TrackTypeId == trackTypeId.Value);

        if (stageId.HasValue)
            query = query.Where(j => j.CurrentStageId == stageId.Value);

        if (assigneeId.HasValue)
            query = query.Where(j => j.AssigneeId == assigneeId.Value);

        if (customerId.HasValue)
            query = query.Where(j => j.CustomerId == customerId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(j =>
                j.Title.ToLower().Contains(term) ||
                j.JobNumber.ToLower().Contains(term));
        }

        var jobs = await query
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
            .Include(j => j.ChildJobs)
            .Include(j => j.ParentJob)
            .Include(j => j.CoverPhotoFile)
            .OrderBy(j => j.CurrentStage.SortOrder)
            .ThenBy(j => j.BoardPosition)
            .ToListAsync(ct);

        return await ToListModelsAsync(jobs, ct);
    }

    public async Task<PagedResponse<JobListResponseModel>> GetPagedJobsAsync(
        JobListQuery query, bool operationTracking, CancellationToken ct)
    {
        // Phase 3 F7-broad / WU-22 — standardised paged-list contract for the
        // jobs table and, with sort=board, the board.
        var q = db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.Customer)
            .Where(j => j.IsArchived == query.IsArchived);

        // — Filters —
        if (query.TrackTypeId.HasValue)
            q = q.Where(j => j.TrackTypeId == query.TrackTypeId.Value);

        if (query.StageId.HasValue)
            q = q.Where(j => j.CurrentStageId == query.StageId.Value);

        if (query.AssigneeId.HasValue)
            q = q.Where(j => j.AssigneeId == query.AssigneeId.Value);

        if (query.CustomerId.HasValue)
            q = q.Where(j => j.CustomerId == query.CustomerId.Value);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            q = q.Where(j =>
                j.Title.ToLower().Contains(term) ||
                j.JobNumber.ToLower().Contains(term) ||
                (j.Part != null && j.Part.PartNumber.ToLower().Contains(term)) ||
                (j.Customer != null && j.Customer.Name.ToLower().Contains(term)));
        }

        if (query.ActiveOnly)
            q = q.Where(j => j.CompletedDate == null && j.Disposition == null);

        if (query.OverdueOnly)
        {
            var startOfToday = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
            q = q.Where(j => j.DueDate != null && j.DueDate < startOfToday && j.CompletedDate == null);
        }

        if (query.OnHoldOnly)
            q = q.Where(j => db.StatusEntries.Any(se =>
                se.EntityType == "Job" &&
                se.EntityId == j.Id &&
                se.Category == "hold" &&
                se.EndedAt == null));

        if (query.DateFrom.HasValue)
            q = q.Where(j => j.CreatedAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(j => j.CreatedAt <= query.DateTo.Value);

        if (query.TeamId is int teamId)
            q = q.Where(operationTracking ? CurrentOperationAtTeam(teamId) : RoutingVisitsTeam(teamId));

        // — Count BEFORE paging —
        var totalCount = await q.CountAsync(ct);

        // — Sort (whitelist; default = createdAt desc) —
        var sortKey = (query.Sort ?? "").Trim().ToLowerInvariant();
        var desc = query.OrderDescending;
        IOrderedQueryable<Job> ordered = sortKey switch
        {
            "name"        => desc ? q.OrderByDescending(j => j.Title)              : q.OrderBy(j => j.Title),
            "title"       => desc ? q.OrderByDescending(j => j.Title)              : q.OrderBy(j => j.Title),
            "jobnumber"   => desc ? q.OrderByDescending(j => j.JobNumber)          : q.OrderBy(j => j.JobNumber),
            "stage"       => desc ? q.OrderByDescending(j => j.CurrentStage.SortOrder) : q.OrderBy(j => j.CurrentStage.SortOrder),
            "board"       => desc
                ? q.OrderByDescending(j => j.CurrentStage.SortOrder).ThenByDescending(j => j.BoardPosition)
                : q.OrderBy(j => j.CurrentStage.SortOrder).ThenBy(j => j.BoardPosition),
            "priority"    => desc ? q.OrderByDescending(j => j.Priority)           : q.OrderBy(j => j.Priority),
            "duedate"     => desc ? q.OrderByDescending(j => j.DueDate)            : q.OrderBy(j => j.DueDate),
            "startdate"   => desc ? q.OrderByDescending(j => j.StartDate)          : q.OrderBy(j => j.StartDate),
            "createdat"   => desc ? q.OrderByDescending(j => j.CreatedAt)          : q.OrderBy(j => j.CreatedAt),
            "updatedat"   => desc ? q.OrderByDescending(j => j.UpdatedAt)          : q.OrderBy(j => j.UpdatedAt),
            "id"          => desc ? q.OrderByDescending(j => j.Id)                 : q.OrderBy(j => j.Id),
            _ => q.OrderByDescending(j => j.CreatedAt),
        };
        ordered = ordered.ThenBy(j => j.Id);

        // — Page slice + load related data for projection —
        var jobs = await ordered
            .Skip(query.Skip)
            .Take(query.EffectivePageSize)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
                    .ThenInclude(so => so.Invoices)
            .Include(j => j.ChildJobs)
            .Include(j => j.ParentJob)
            .Include(j => j.CoverPhotoFile)
            .ToListAsync(ct);

        var items = await ToListModelsAsync(jobs, ct);

        return new PagedResponse<JobListResponseModel>(
            items,
            totalCount,
            query.EffectivePage,
            query.EffectivePageSize);
    }

    private Expression<Func<Job, bool>> RoutingVisitsTeam(int teamId) =>
        j => db.Operations.Any(o => o.PartId == j.PartId
            && o.WorkCenter != null && o.WorkCenter.TeamId == teamId);

    private Expression<Func<Job, bool>> CurrentOperationAtTeam(int teamId)
    {
        const JobOperationStatus complete = JobOperationStatus.Complete;
        const JobOperationStatus skipped = JobOperationStatus.Skipped;
        const JobOperationStatus inProgress = JobOperationStatus.InProgress;

        return j => db.Operations.Any(o => o.PartId == j.PartId
            && o.WorkCenter != null && o.WorkCenter.TeamId == teamId
            && !db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id
                && (r.Status == complete || r.Status == skipped))
            && (db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == o.Id && r.Status == inProgress)
                || db.TimeEntries.Any(t => t.JobId == j.Id && t.OperationId == o.Id
                    && t.TimerStart != null && t.TimerStop == null)
                || (!db.Operations.Any(p => p.PartId == j.PartId
                        && !db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == p.Id
                            && (r.Status == complete || r.Status == skipped))
                        && (db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == p.Id && r.Status == inProgress)
                            || db.TimeEntries.Any(t => t.JobId == j.Id && t.OperationId == p.Id
                                && t.TimerStart != null && t.TimerStop == null)))
                    && !db.Operations.Any(p => p.PartId == j.PartId
                        && (p.StepNumber < o.StepNumber || (p.StepNumber == o.StepNumber && p.Id < o.Id))
                        && !db.JobOperations.Any(r => r.JobId == j.Id && r.OperationId == p.Id
                            && (r.Status == complete || r.Status == skipped))))));
    }

    private async Task<List<JobListResponseModel>> ToListModelsAsync(List<Job> jobs, CancellationToken ct)
    {
        // Load assignee info separately (ApplicationUser is in data layer)
        var assigneeIds = jobs
            .Where(j => j.AssigneeId.HasValue)
            .Select(j => j.AssigneeId!.Value)
            .Distinct()
            .ToList();

        var assignees = assigneeIds.Count > 0
            ? await db.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct)
            : [];

        // Load active holds for all jobs in the result set
        var jobIds = jobs.Select(j => j.Id).ToList();
        var activeHoldsByJobId = jobIds.Count > 0
            ? await db.StatusEntries
                .Where(se =>
                    se.EntityType == "Job" &&
                    jobIds.Contains(se.EntityId) &&
                    se.Category == "hold" &&
                    se.EndedAt == null)
                .GroupBy(se => se.EntityId)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.Select(se => se.StatusLabel).ToList(),
                    ct)
            : [];

        var makingByJobId = jobIds.Count > 0
            ? await db.Jobs
                .Where(j => jobIds.Contains(j.Id))
                .Select(j => new
                {
                    j.Id,
                    PartNumber = j.Part != null ? j.Part.PartNumber : null,
                    Quantity = j.JobParts.Any(jp => jp.PartId == j.PartId)
                        ? (decimal?)j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => jp.Quantity)
                        : j.SalesOrderLineId != null
                            ? j.SalesOrderLine!.Quantity
                            : null,
                })
                .ToDictionaryAsync(x => x.Id, ct)
            : [];

        var today = clock.UtcNow.UtcDateTime.Date;

        return jobs.Select(j =>
        {
            var assignee = j.AssigneeId.HasValue && assignees.TryGetValue(j.AssigneeId.Value, out var u) ? u : null;
            string? billingStatus = null;
            if (j.CompletedDate != null)
            {
                var hasInvoice = j.SalesOrderLine?.SalesOrder?.Invoices?.Any() == true;
                billingStatus = hasInvoice ? "Invoiced" : "Uninvoiced";
            }

            var activeHolds = activeHoldsByJobId.TryGetValue(j.Id, out var holds) ? holds : [];
            var making = makingByJobId.GetValueOrDefault(j.Id);

            return new JobListResponseModel(
                j.Id,
                j.JobNumber,
                j.Title,
                j.CurrentStage.Name,
                j.CurrentStage.Color,
                j.AssigneeId,
                assignee?.Initials,
                assignee?.AvatarColor,
                j.Priority.ToString(),
                j.DueDate,
                j.DueDate.HasValue && j.DueDate.Value.UtcDateTime.Date < today && j.CompletedDate == null,
                j.Customer?.Name,
                billingStatus,
                j.Disposition?.ToString(),
                j.ChildJobs.Count(c => c.DeletedAt == null),
                j.ExternalRef,
                null,
                activeHolds,
                j.CoverPhotoFileId.HasValue ? $"/api/v1/files/{j.CoverPhotoFileId}" : null,
                j.ParentJobId,
                j.ParentJob?.JobNumber,
                j.CustomerId,
                j.SalesOrderLine?.SalesOrderId,
                j.SalesOrderLine?.SalesOrder?.OrderNumber,
                j.BoardPosition,
                making?.PartNumber,
                making?.Quantity);
        }).ToList();
    }

    public async Task<JobDetailResponseModel?> GetDetailAsync(int id, CancellationToken ct)
    {
        var job = await db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.TrackType)
            .Include(j => j.Customer)
            .Include(j => j.Part)
            .Include(j => j.ParentJob)
            .Include(j => j.ChildJobs)
            .Include(j => j.CoverPhotoFile)
            .Include(j => j.SalesOrderLine)
                .ThenInclude(sol => sol!.SalesOrder)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job is null) return null;

        ApplicationUser? assignee = null;
        if (job.AssigneeId.HasValue)
            assignee = await db.Users.FirstOrDefaultAsync(u => u.Id == job.AssigneeId.Value, ct);

        return new JobDetailResponseModel(
            job.Id,
            job.JobNumber,
            job.Title,
            job.Description,
            job.TrackTypeId,
            job.TrackType.Name,
            job.CurrentStageId,
            job.CurrentStage.Name,
            job.CurrentStage.Color,
            job.AssigneeId,
            assignee?.Initials,
            assignee is not null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : null,
            assignee?.AvatarColor,
            job.Priority.ToString(),
            job.CustomerId,
            job.Customer?.Name,
            job.DueDate,
            job.StartDate,
            job.CompletedDate,
            job.IsArchived,
            job.BoardPosition,
            job.IterationCount,
            job.IterationNotes,
            job.ExternalId,
            job.ExternalRef,
            job.Provider,
            job.Disposition?.ToString(),
            job.DispositionNotes,
            job.DispositionAt,
            job.PartId,
            job.Part?.PartNumber,
            job.ParentJobId,
            job.ParentJob?.JobNumber,
            job.ChildJobs.Count(c => c.DeletedAt == null),
            job.CreatedAt,
            job.UpdatedAt,
            job.CoverPhotoFileId.HasValue ? $"/api/v1/files/{job.CoverPhotoFileId}" : null,
            job.Version,
            job.SalesOrderLine?.SalesOrderId,
            job.SalesOrderLine?.SalesOrder?.OrderNumber,
            job.PartRevision);
    }

    public async Task<Job?> FindAsync(int id, CancellationToken ct)
    {
        return await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public Task<bool> JobNumberExistsAsync(string jobNumber, int? excludeId, CancellationToken ct)
    {
        var query = db.Jobs.Where(j => j.JobNumber == jobNumber);
        if (excludeId.HasValue)
            query = query.Where(j => j.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task<string> GenerateNextJobNumberAsync(CancellationToken ct)
    {
        // Use a PostgreSQL sequence for atomic, concurrent-safe job number generation.
        var nextVal = await db.Database
            .SqlQueryRaw<int>("SELECT CAST(nextval('job_number_seq') AS int) AS \"Value\"")
            .SingleAsync(ct);

        return $"J-{nextVal}";
    }

    public async Task<int> GetMaxBoardPositionAsync(int stageId, CancellationToken ct)
    {
        return await db.Jobs
            .Where(j => j.CurrentStageId == stageId)
            .MaxAsync(j => (int?)j.BoardPosition, ct) ?? 0;
    }

    public async Task<List<Job>> FindMultipleAsync(List<int> ids, CancellationToken ct)
    {
        return await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => ids.Contains(j.Id))
            .ToListAsync(ct);
    }

    public async Task<List<ChildJobResponseModel>> GetChildJobsAsync(int parentJobId, CancellationToken ct)
    {
        return await db.Jobs
            .Where(j => j.ParentJobId == parentJobId)
            .Include(j => j.CurrentStage)
            .Include(j => j.Part)
            .Include(j => j.JobParts)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new ChildJobResponseModel(
                j.Id,
                j.JobNumber,
                j.Title,
                j.CurrentStage.Name,
                j.Part != null ? j.Part.PartNumber : null,
                j.JobParts.Select(jp => (decimal?)jp.Quantity).FirstOrDefault(),
                j.DueDate,
                j.CreatedAt))
            .ToListAsync(ct);
    }

    public Task AddAsync(Job job, CancellationToken ct)
    {
        db.Jobs.Add(job);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
