using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class BackflushService(
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    ISettingsService settings,
    IUomService uom,
    IMediator mediator) : IBackflushService
{
    public const string Capability = "CAP-MFG-BACKFLUSH";
    public const string NotePrefix = "Backflush";

    public async Task BackflushJobAsync(int jobId, int userId, DateTimeOffset now, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(Capability))
            return;

        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId && !j.IsArchived)
            .Select(j => new { j.Id, j.JobNumber, j.PartId })
            .FirstOrDefaultAsync(ct);
        if (job?.PartId is not int parentPartId)
            return;

        var reported = await ReportedQuantityAsync(job.Id, parentPartId, ct);
        if (reported <= 0m)
            return;

        var lines = await db.BOMLines.AsNoTracking()
            .Where(b => b.ParentPartId == parentPartId)
            .Select(b => new
            {
                b.ChildPartId,
                b.Quantity,
                b.UomId,
                b.ChildPart.PartNumber,
                b.ChildPart.BackflushPolicy,
                b.ChildPart.StockUomId,
                b.ChildPart.DefaultBinId,
            })
            .ToListAsync(ct);
        if (lines.Count == 0)
            return;

        var plantDefault = await PlantDefaultAsync(ct);
        var shopToday = await LotExpiry.ShopTodayAsync(db, now, ct);

        foreach (var component in lines.Where(l => (l.BackflushPolicy ?? plantDefault) == BackflushPolicy.Auto)
                     .GroupBy(l => l.ChildPartId))
        {
            var first = component.First();
            decimal perUnit = 0m;
            var convertible = true;
            foreach (var line in component)
            {
                if (await ToStockUomAsync(line.Quantity, line.UomId, line.StockUomId, line.ChildPartId, ct) is decimal converted)
                    perUnit += converted;
                else
                    convertible = false;
            }

            if (!convertible)
            {
                Log(job.Id, userId, now, $"Backflush skipped {first.PartNumber}: no unit conversion from the BOM unit to its stock unit.");
                continue;
            }

            var consumed = await db.MaterialIssues.AsNoTracking()
                .Where(mi => mi.JobId == job.Id && mi.PartId == first.ChildPartId
                    && mi.IssueType == MaterialIssueType.Issue
                    && mi.Notes != null && mi.Notes.StartsWith(NotePrefix))
                .SumAsync(mi => mi.Quantity, ct);
            var due = perUnit * reported - consumed;
            if (due <= 0m)
                continue;

            var shortBy = await ConsumeAsync(job.Id, first.ChildPartId, first.DefaultBinId, due, reported, userId, now, shopToday, ct);
            if (shortBy > 0m)
                Log(job.Id, userId, now, $"Backflush short: {first.PartNumber} {shortBy:0.####} not in stock.");
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<decimal> ReportedQuantityAsync(int jobId, int parentPartId, CancellationToken ct)
    {
        var fromRuns = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.JobId == jobId && r.ReceivedToStockAt != null)
            .SumAsync(r => r.CompletedQuantity + r.ScrapQuantity, ct);

        var lastStepId = await db.Operations.AsNoTracking()
            .Where(o => o.PartId == parentPartId)
            .OrderByDescending(o => o.StepNumber)
            .ThenByDescending(o => o.Id)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync(ct);
        var fromLastStep = lastStepId is int stepId
            ? await db.JobOperations.AsNoTracking()
                .Where(r => r.JobId == jobId && r.OperationId == stepId && r.Status == JobOperationStatus.Complete)
                .Select(r => r.CompletedQuantity + r.ScrapQuantity)
                .FirstOrDefaultAsync(ct)
            : 0m;

        return Math.Max(fromRuns, fromLastStep);
    }

    private async Task<BackflushPolicy> PlantDefaultAsync(CancellationToken ct)
        => Enum.TryParse<BackflushPolicy>(
            await settings.GetStringAsync(ShopFloorSettings.BackflushDefaultKey, ct), ignoreCase: true, out var policy)
            ? policy
            : BackflushPolicy.Manual;

    private async Task<decimal?> ToStockUomAsync(decimal quantity, int? bomUomId, int? stockUomId, int partId, CancellationToken ct)
        => bomUomId is int from && stockUomId is int to && from != to
            ? await uom.TryConvertAsync(quantity, from, to, partId, ct)
            : quantity;

    private async Task<decimal> ConsumeAsync(
        int jobId, int partId, int? defaultBinId, decimal quantity, decimal reported, int userId,
        DateTimeOffset now, DateOnly shopToday, CancellationToken ct)
    {
        var locationIds = await db.BinContents.AsNoTracking()
            .Where(bc => bc.EntityType == "part" && bc.EntityId == partId && bc.RemovedAt == null && bc.Quantity > 0
                && bc.Status != BinContentStatus.QcHold)
            .Select(bc => bc.LocationId)
            .Distinct()
            .ToListAsync(ct);
        var expiredLots = await LotExpiry.ExpiredLotsAsync(db, partId, shopToday, ct);
        var remaining = quantity;

        foreach (var locationId in locationIds.OrderBy(id => id != defaultBinId).ThenBy(id => id))
        {
            if (remaining <= 0m)
                break;

            var rows = (await ScanBinStock.ActiveRowsAsync(db, partId, locationId, ct))
                .Where(bc => bc.Status != BinContentStatus.QcHold
                    && (bc.LotNumber is null || !expiredLots.ContainsKey(bc.LotNumber)))
                .ToList();
            var drawn = BinContentDrawDown.Take(rows, remaining, userId, now);
            if (drawn.Count == 0)
                continue;

            foreach (var (row, taken) in drawn)
            {
                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = partId,
                    Quantity = taken,
                    LotNumber = row.LotNumber,
                    FromLocationId = locationId,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.Issue,
                });
                remaining -= taken;
            }

            foreach (var (row, taken) in drawn)
            {
                await mediator.Send(new CreateMaterialIssueCommand(
                    jobId, partId, OperationId: null, taken,
                    BinContentId: row.Id, StorageLocationId: locationId, LotNumber: row.LotNumber,
                    MaterialIssueType.Issue, $"{NotePrefix} ({reported:0.####} reported)", userId,
                    StockDrawnByCaller: true), ct);
            }
        }

        return remaining;
    }

    private void Log(int jobId, int userId, DateTimeOffset now, string description)
        => db.JobActivityLogs.Add(new JobActivityLog
        {
            JobId = jobId,
            UserId = userId,
            Action = ActivityAction.FieldChanged,
            FieldName = "Backflush",
            Description = description,
            CreatedAt = now,
        });
}
