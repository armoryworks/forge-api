using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

/// <summary>
/// The jobs a part's lot was issued to (material issues that name the lot). Lot consumption edges are only
/// written by hand, so this is how lot trace and recall reach the work that used a purchased lot; what the job
/// made is not lot-stamped, so anything reached this way is approximate.
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
}
