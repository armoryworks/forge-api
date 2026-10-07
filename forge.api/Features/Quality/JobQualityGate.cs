using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public static class JobQualityGate
{
    private const string Failed = "Failed";
    private const string Passed = "Passed";

    public static string BlockedMessage(string blocking) =>
        "Cannot complete this job while it has an unresolved non-conformance (NCR) or a failed QC " +
        $"inspection ({blocking}). Disposition or close each NCR, and re-inspect or disposition each " +
        "failed inspection, before advancing to the final stage.";

    public static async Task<Dictionary<int, string>> FindBlockersAsync(
        AppDbContext db, IReadOnlyCollection<int> jobIds, CancellationToken cancellationToken)
    {
        var blockers = new Dictionary<int, string>();
        if (jobIds.Count == 0)
            return blockers;

        var unresolvedNcrs = await db.NonConformances
            .AsNoTracking()
            .Where(n => n.JobId != null
                && jobIds.Contains(n.JobId.Value)
                && n.Status != NcrStatus.Dispositioned
                && n.Status != NcrStatus.Closed)
            .Select(n => new { JobId = n.JobId!.Value, n.NcrNumber })
            .ToListAsync(cancellationToken);

        var inspections = await db.QcInspections
            .AsNoTracking()
            .Where(i => i.JobId != null
                && jobIds.Contains(i.JobId.Value)
                && (i.Status == Failed || i.Status == Passed))
            .Select(i => new
            {
                i.Id,
                JobId = i.JobId!.Value,
                i.TemplateId,
                i.Status,
                CompletedAt = i.CompletedAt ?? i.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var failed = inspections.Where(i => i.Status == Failed).ToList();
        var failedIds = failed.Select(i => i.Id).ToList();

        var dispositionedInspectionIds = failedIds.Count == 0
            ? []
            : (await db.NonConformances
                .AsNoTracking()
                .Where(n => n.QcInspectionId != null
                    && failedIds.Contains(n.QcInspectionId.Value)
                    && (n.Status == NcrStatus.Dispositioned || n.Status == NcrStatus.Closed))
                .Select(n => n.QcInspectionId!.Value)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        var unresolvedFailed = failed
            .Where(f => !dispositionedInspectionIds.Contains(f.Id)
                && !inspections.Any(p => p.Status == Passed
                    && p.JobId == f.JobId
                    && p.TemplateId == f.TemplateId
                    && p.CompletedAt > f.CompletedAt))
            .ToList();

        foreach (var jobId in jobIds.Distinct())
        {
            var ncrNumbers = unresolvedNcrs
                .Where(n => n.JobId == jobId)
                .Select(n => n.NcrNumber)
                .Order()
                .ToList();
            var inspectionIds = unresolvedFailed
                .Where(i => i.JobId == jobId)
                .Select(i => i.Id)
                .Order()
                .ToList();

            var parts = new List<string>();
            if (ncrNumbers.Count > 0)
                parts.Add($"unresolved NCR{(ncrNumbers.Count == 1 ? "" : "s")} {string.Join(", ", ncrNumbers)}");
            if (inspectionIds.Count > 0)
                parts.Add($"failed QC inspection{(inspectionIds.Count == 1 ? "" : "s")} " +
                    string.Join(", ", inspectionIds.Select(id => $"#{id}")));

            if (parts.Count > 0)
                blockers[jobId] = string.Join("; ", parts);
        }

        return blockers;
    }
}
