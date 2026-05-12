using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class JobLinkRepository(AppDbContext db) : IJobLinkRepository
{
    public async Task<List<JobLinkResponseModel>> GetByJobIdAsync(int jobId, CancellationToken ct)
    {
        var links = await db.JobLinks
            .Include(l => l.SourceJob).ThenInclude(j => j.CurrentStage)
            .Include(l => l.TargetJob).ThenInclude(j => j.CurrentStage)
            .Where(l => l.SourceJobId == jobId || l.TargetJobId == jobId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        return links.Select(l =>
        {
            var isSource = l.SourceJobId == jobId;
            var linkedJob = isSource ? l.TargetJob : l.SourceJob;
            var linkType = isSource ? l.LinkType : GetInverseType(l.LinkType);

            return new JobLinkResponseModel(
                l.Id,
                l.SourceJobId,
                l.TargetJobId,
                linkType.ToString(),
                linkedJob.Id,
                linkedJob.JobNumber,
                linkedJob.Title,
                linkedJob.CurrentStage.Name,
                linkedJob.CurrentStage.Color,
                l.CreatedAt);
        }).ToList();
    }

    public async Task<JobLink?> FindAsync(int linkId, CancellationToken ct)
    {
        return await db.JobLinks.FirstOrDefaultAsync(l => l.Id == linkId, ct);
    }

    public async Task<bool> JobExistsAsync(int jobId, CancellationToken ct)
    {
        return await db.Jobs.AnyAsync(j => j.Id == jobId, ct);
    }

    public async Task<bool> LinkExistsAsync(int sourceJobId, int targetJobId, JobLinkType linkType, CancellationToken ct)
    {
        var inverseType = GetInverseType(linkType);
        return await db.JobLinks.AnyAsync(l =>
            (l.SourceJobId == sourceJobId && l.TargetJobId == targetJobId && l.LinkType == linkType) ||
            (l.SourceJobId == targetJobId && l.TargetJobId == sourceJobId && l.LinkType == inverseType),
            ct);
    }

    public Task AddAsync(JobLink link, CancellationToken ct)
    {
        db.JobLinks.Add(link);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(JobLink link, CancellationToken ct)
    {
        db.JobLinks.Remove(link);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }

    private static JobLinkType GetInverseType(JobLinkType type) => type switch
    {
        JobLinkType.Blocks => JobLinkType.BlockedBy,
        JobLinkType.BlockedBy => JobLinkType.Blocks,
        JobLinkType.Parent => JobLinkType.Child,
        JobLinkType.Child => JobLinkType.Parent,
        _ => type,
    };
}
