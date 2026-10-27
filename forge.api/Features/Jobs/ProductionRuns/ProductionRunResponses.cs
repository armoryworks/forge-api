using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// Builds <see cref="ProductionRunResponseModel"/>s with the names the run screen shows: operator, work center,
/// tooling, and the number of the finished-goods receipt journal when one was posted for the run.
/// </summary>
public static class ProductionRunResponses
{
    public const string JournalSourceType = "ProductionRun";

    public static async Task<ProductionRunResponseModel> BuildAsync(
        AppDbContext db, ProductionRun run, CancellationToken ct) =>
        (await BuildManyAsync(db, [run], ct))[0];

    public static async Task<List<ProductionRunResponseModel>> BuildManyAsync(
        AppDbContext db, IReadOnlyList<ProductionRun> runs, CancellationToken ct)
    {
        if (runs.Count == 0)
            return [];

        var operatorIds = runs.Where(r => r.OperatorId.HasValue).Select(r => r.OperatorId!.Value).Distinct().ToList();
        var operators = operatorIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => operatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct)
            : [];

        var workCenterIds = runs.Where(r => r.WorkCenterId.HasValue).Select(r => r.WorkCenterId!.Value).Distinct().ToList();
        var workCenters = workCenterIds.Count > 0
            ? await db.WorkCenters.AsNoTracking()
                .Where(w => workCenterIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name, ct)
            : [];

        var toolingIds = runs.Where(r => r.ToolingAssetId.HasValue).Select(r => r.ToolingAssetId!.Value).Distinct().ToList();
        var tooling = toolingIds.Count > 0
            ? await db.Assets.AsNoTracking()
                .Where(a => toolingIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => new { a.Name, a.CavityCount }, ct)
            : [];

        var receivedIds = runs.Where(r => r.ReceivedToStockAt.HasValue).Select(r => (long)r.Id).ToList();
        var journals = receivedIds.Count > 0
            ? (await db.JournalEntries.AsNoTracking()
                .Where(e => e.SourceType == JournalSourceType && e.SourceId != null && receivedIds.Contains(e.SourceId.Value))
                .Select(e => new { SourceId = e.SourceId!.Value, e.Id, e.EntryNumber })
                .ToListAsync(ct))
                .GroupBy(e => e.SourceId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).First().EntryNumber)
            : [];

        return runs.Select(run =>
        {
            var tool = run.ToolingAssetId is int toolId && tooling.TryGetValue(toolId, out var t) ? t : null;
            return new ProductionRunResponseModel(
                run.Id,
                run.JobId,
                run.Job.JobNumber,
                run.PartId,
                run.Part.PartNumber,
                run.Part.Description ?? run.Part.Name,
                run.OperatorId,
                run.OperatorId is int op && operators.TryGetValue(op, out var name) ? name : null,
                run.RunNumber,
                run.TargetQuantity,
                run.CompletedQuantity,
                run.ScrapQuantity,
                run.Status.ToString(),
                run.StartedAt,
                run.CompletedAt,
                run.Notes,
                run.SetupTimeMinutes,
                run.RunTimeMinutes,
                ProductionRun.YieldPercent(run.CompletedQuantity, run.ScrapQuantity),
                run.ReceivedQuantity,
                run.ReceivedToStockAt,
                run.WorkCenterId,
                run.WorkCenterId is int wc && workCenters.TryGetValue(wc, out var wcName) ? wcName : null,
                run.ToolingAssetId,
                tool?.Name,
                tool?.CavityCount,
                run.ShotsApplied,
                run.RunTimeManual,
                journals.TryGetValue(run.Id, out var entryNumber) ? entryNumber : null);
        }).ToList();
    }
}
