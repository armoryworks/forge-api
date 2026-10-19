using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

/// <summary>
/// The jobs a part's lot was issued to (material issues that name the lot). Lot consumption edges are only
/// written by hand, so this is how lot trace and recall reach the work that used a purchased lot; what the job
/// made is not lot-stamped, so anything reached this way is approximate. A lot issued to a sub-assembly job also
/// went into its parent jobs, so recall follows a job up to its parents and serial genealogy follows a job down
/// to its sub-assembly jobs.
/// </summary>
public static class LotMaterialIssues
{
    public sealed record IssuedJob(int JobId, string JobNumber, string JobTitle, DateTimeOffset IssuedAt, decimal Quantity);

    public static async Task<List<int>> JobIdsAsync(
        AppDbContext db, IReadOnlySet<(int PartId, string LotNumber)> partLots, CancellationToken ct)
    {
        if (partLots.Count == 0)
            return [];

        var partIds = partLots.Select(p => p.PartId).Distinct().ToList();
        var lotNumbers = partLots.Select(p => p.LotNumber).Distinct().ToList();
        var rows = await db.MaterialIssues
            .AsNoTracking()
            .Where(mi => mi.IssueType == MaterialIssueType.Issue
                && mi.LotNumber != null
                && partIds.Contains(mi.PartId)
                && lotNumbers.Contains(mi.LotNumber))
            .Select(mi => new { mi.JobId, mi.PartId, mi.LotNumber })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .Where(r => partLots.Contains((r.PartId, r.LotNumber!)))
            .Select(r => r.JobId)
            .Distinct()
            .ToList();
    }

    public static Task<List<IssuedJob>> ForLotAsync(AppDbContext db, int partId, string lotNumber, CancellationToken ct)
        => db.MaterialIssues
            .AsNoTracking()
            .Where(mi => mi.IssueType == MaterialIssueType.Issue && mi.PartId == partId && mi.LotNumber == lotNumber)
            .OrderBy(mi => mi.IssuedAt)
            .Select(mi => new IssuedJob(mi.JobId, mi.Job.JobNumber, mi.Job.Title, mi.IssuedAt, mi.Quantity))
            .ToListAsync(ct);

    public static Task<List<int>> WithParentJobsAsync(AppDbContext db, IEnumerable<int> jobIds, CancellationToken ct)
        => WalkAsync(jobIds, ids => db.Jobs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id) && j.ParentJobId != null)
            .Select(j => j.ParentJobId!.Value)
            .ToListAsync(ct));

    public static Task<List<int>> WithSubAssemblyJobsAsync(AppDbContext db, int jobId, CancellationToken ct)
        => WalkAsync([jobId], ids => db.Jobs
            .AsNoTracking()
            .Where(j => j.ParentJobId != null && ids.Contains(j.ParentJobId.Value))
            .Select(j => j.Id)
            .ToListAsync(ct));

    private static async Task<List<int>> WalkAsync(IEnumerable<int> start, Func<List<int>, Task<List<int>>> next)
    {
        var seen = new HashSet<int>(start);
        var frontier = seen.ToList();
        while (frontier.Count > 0)
            frontier = (await next(frontier)).Where(seen.Add).ToList();
        return seen.ToList();
    }
}
