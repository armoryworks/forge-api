using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// The tooling (mold, die) a production run uses and the shots the run puts on it. A run of <c>n</c> pieces
/// (good + scrap) on a tool with <c>c</c> cavities takes <c>ceil(n / c)</c> shots; a tool with no cavity count
/// is treated as single-cavity. The shots already added are kept on the run (<see cref="ProductionRun.ShotsApplied"/>),
/// so every quantity change adds only the difference to the tool's <see cref="Asset.CurrentShotCount"/>, and
/// moving the run to another tool or deleting it takes its shots back off.
/// </summary>
public static class ProductionRunTooling
{
    public static int ShotsFor(int goodQuantity, int scrapQuantity, int? cavityCount)
    {
        var pieces = goodQuantity + scrapQuantity;
        if (pieces <= 0)
            return 0;

        var cavities = cavityCount is > 0 ? cavityCount.Value : 1;
        return (pieces + cavities - 1) / cavities;
    }

    /// <summary>The tool a new run defaults to: the part's tooling, else the tooling asset on the part's first
    /// routing step at the run's work center (or on any step when the work center is unknown), else the one
    /// tooling asset in service at the run's work center (none when that work center holds several).</summary>
    public static async Task<int?> DefaultToolingAsync(
        AppDbContext db, Part part, int? workCenterId, CancellationToken ct)
    {
        if (part.ToolingAssetId is int partTool
            && await db.Assets.AsNoTracking().AnyAsync(a => a.Id == partTool && a.AssetType == AssetType.Tooling, ct))
            return partTool;

        var routingTool = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == part.Id
                && o.AssetId != null
                && o.Asset!.AssetType == AssetType.Tooling
                && (workCenterId == null || o.WorkCenterId == workCenterId))
            .OrderBy(o => o.StepNumber)
            .ThenBy(o => o.Id)
            .Select(o => o.AssetId)
            .FirstOrDefaultAsync(ct);
        if (routingTool is not null || workCenterId is null)
            return routingTool;

        var stationTools = await db.Assets.AsNoTracking()
            .Where(a => a.WorkCenterId == workCenterId
                && a.AssetType == AssetType.Tooling
                && a.Status != AssetStatus.Retired
                && a.Status != AssetStatus.OutOfService)
            .Select(a => a.Id)
            .Take(2)
            .ToListAsync(ct);
        return stationTools.Count == 1 ? stationTools[0] : null;
    }

    /// <summary>Loads the tooling asset and refuses anything that is not tooling.</summary>
    public static async Task<Asset> RequireToolingAsync(AppDbContext db, int assetId, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new KeyNotFoundException($"Asset {assetId} not found.");
        if (asset.AssetType != AssetType.Tooling)
            throw new InvalidOperationException($"Asset {asset.Name} is not tooling, so it cannot be a run's tooling.");
        return asset;
    }

    /// <summary>Moves the run to <paramref name="toolingAssetId"/>, taking the shots it put on its previous tool
    /// back off that tool. A no-op when the tool does not change.</summary>
    public static async Task ReassignAsync(AppDbContext db, ProductionRun run, int? toolingAssetId, CancellationToken ct)
    {
        if (run.ToolingAssetId == toolingAssetId)
            return;

        if (toolingAssetId is int newTool)
            await RequireToolingAsync(db, newTool, ct);

        await RemoveShotsAsync(db, run, ct);
        run.ToolingAssetId = toolingAssetId;
    }

    /// <summary>Brings the tool's shot count in line with the run's current quantities. Returns the shots added
    /// (negative when a correction lowered the quantities).</summary>
    public static async Task<int> ApplyShotsAsync(AppDbContext db, ProductionRun run, CancellationToken ct)
    {
        if (run.ToolingAssetId is not int toolId)
            return 0;

        var tool = await db.Assets.FirstOrDefaultAsync(a => a.Id == toolId, ct);
        if (tool is null)
            return 0;

        var target = ShotsFor(run.CompletedQuantity, run.ScrapQuantity, tool.CavityCount);
        var delta = target - run.ShotsApplied;
        if (delta == 0)
            return 0;

        tool.CurrentShotCount = Math.Max(0, tool.CurrentShotCount + delta);
        run.ShotsApplied = target;
        return delta;
    }

    /// <summary>Takes every shot the run added back off its tool.</summary>
    public static async Task RemoveShotsAsync(AppDbContext db, ProductionRun run, CancellationToken ct)
    {
        if (run.ShotsApplied == 0 || run.ToolingAssetId is not int toolId)
        {
            run.ShotsApplied = 0;
            return;
        }

        var tool = await db.Assets.FirstOrDefaultAsync(a => a.Id == toolId, ct);
        if (tool is not null)
            tool.CurrentShotCount = Math.Max(0, tool.CurrentShotCount - run.ShotsApplied);
        run.ShotsApplied = 0;
    }
}
