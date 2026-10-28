using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.Lots;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// Receive a completed production run's good output into finished-goods stock — the explicit job-complete→FG
/// step (separate from flipping the run to Completed). Stocks the chosen bin operationally and, when
/// CAP-ACCT-FULLGL is on, posts Dr INVENTORY_FG / Cr INVENTORY_WIP at standard cost + feeds the FG valuation
/// store. A lot-tracked part (with CAP-INV-LOTS on) is stocked under the run's lot: the lot record already
/// made for the run, else a new one with the next house lot number. Idempotent: a run already received to stock is a
/// no-op (returns current state).
/// </summary>
public record ReceiveProductionRunToStockCommand(
    int JobId, int RunId, int ReceivedByUserId, int? LocationId = null, int? SurplusLocationId = null)
    : IRequest<ProductionRunResponseModel>;

public class ReceiveProductionRunToStockHandler(
    AppDbContext db,
    IClock clock,
    ICapabilitySnapshotProvider capabilities,
    // Operational FG stock-in (creates/increments BinContent + a Receive movement). Null in mock-based unit
    // tests → no stock movement, run is still stamped received.
    IInventoryRepository? inventory = null,
    // Phase-2 STAGE E — inline FG/WIP GL posting; null/default keeps the handler constructible without an
    // accounting context, and it no-ops while CAP-ACCT-FULLGL is off.
    IProductionReceiptPostingService? posting = null,
    IBackflushService? backflush = null,
    IBarcodeService? barcodes = null,
    ISettingsService? settings = null)
    : IRequestHandler<ReceiveProductionRunToStockCommand, ProductionRunResponseModel>
{
    public const string MultiLocationCapability = "CAP-INV-MULTILOC";

    public async Task<ProductionRunResponseModel> Handle(
        ReceiveProductionRunToStockCommand request, CancellationToken cancellationToken)
    {
        var run = await db.ProductionRuns
            .Include(pr => pr.Job)
            .Include(pr => pr.Part)
            .FirstOrDefaultAsync(pr => pr.Id == request.RunId && pr.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Production run {request.RunId} not found on job {request.JobId}.");

        // Idempotent: already received → no-op (don't re-stock or re-post).
        if (run.ReceivedToStockAt is null)
        {
            if (run.Status != ProductionRunStatus.Completed)
                throw new InvalidOperationException(
                    $"Only completed production runs can be received to stock (run {run.Id} is {run.Status}).");

            // Good output = CompletedQuantity. The UpdateProductionRun validator keeps CompletedQuantity and
            // ScrapQuantity disjoint (Completed + Scrap ≤ Target), so CompletedQuantity is the good quantity.
            var goodQty = run.CompletedQuantity;
            if (goodQty <= 0)
                throw new InvalidOperationException(
                    $"Production run {run.Id} has no good completed quantity to receive into stock.");

            var receivedAt = clock.UtcNow;

            // One transaction: the stock-in, the received stamp AND the inline FG/WIP posting commit (or roll
            // back) together. Npgsql opens a real transaction; the in-memory test provider treats it as a no-op.
            // tx is opened only when posting is wired and no caller's transaction is already open.
            await using var tx = (posting is not null || backflush is not null) && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var lotNumber = await ResolveRunLotAsync(run, goodQty, cancellationToken);

            // Operational FG stock-in (not CAP-ACCT-FULLGL gated): find-or-create the active BinContent for
            // (part, bin, lot) and increment it, then record a Receive movement.
            string? binName = null;
            string surplusNote = string.Empty;
            if (inventory is not null)
            {
                var location = await ResolveReceivingBinAsync(
                    inventory, request.LocationId, run.Part.DefaultBinId, cancellationToken);
                binName = location.Name;

                var split = await SurplusStock.PlanAsync(
                    db, settings, run.JobId, run.PartId, goodQty, request.SurplusLocationId, run.Id, cancellationToken);
                var surplusQty = split.SurplusLocation is { } surplusBin && surplusBin.Id != location.Id
                    ? (int)split.SurplusQuantity
                    : 0;

                if (goodQty - surplusQty > 0)
                    await StockAsync(inventory, run, location.Id, goodQty - surplusQty, lotNumber,
                        request.ReceivedByUserId, receivedAt, cancellationToken);
                if (surplusQty > 0)
                {
                    await StockAsync(inventory, run, split.SurplusLocation!.Id, surplusQty, lotNumber,
                        request.ReceivedByUserId, receivedAt, cancellationToken);
                    surplusNote = $" {surplusQty} beyond the order quantity went to the surplus location {split.SurplusLocation.Name}.";
                }
            }

            run.ReceivedQuantity = goodQty;
            run.ReceivedToStockAt = receivedAt;

            db.JobActivityLogs.Add(new JobActivityLog
            {
                JobId = run.JobId,
                UserId = request.ReceivedByUserId,
                Action = ActivityAction.StatusChanged,
                Description = binName is null
                    ? $"Received {goodQty} of {run.Part.PartNumber} from run {run.RunNumber} to stock."
                    : $"Received {goodQty} of {run.Part.PartNumber} from run {run.RunNumber} into {binName}.{surplusNote}",
                CreatedAt = receivedAt,
            });

            await db.SaveChangesAsync(cancellationToken);

            if (posting is not null)
            {
                var entryDate = DateOnly.FromDateTime(run.ReceivedToStockAt.Value.UtcDateTime);
                await posting.PostProductionReceiptAsync(
                    run.Id, entryDate, request.ReceivedByUserId, cancellationToken);
            }

            if (backflush is not null)
                await backflush.BackflushJobAsync(run.JobId, request.ReceivedByUserId, receivedAt, cancellationToken);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
        }

        return await BuildResponseAsync(run, cancellationToken);
    }

    private static async Task StockAsync(
        IInventoryRepository inventory, ProductionRun run, int locationId, int quantity, string? lotNumber,
        int userId, DateTimeOffset at, CancellationToken ct)
    {
        var existing = await inventory.FindActiveBinContentByPartLocationLotAsync(
            run.PartId, locationId, lotNumber, BinContentStatus.Stored, ct);
        if (existing is not null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            await inventory.AddBinContentAsync(new BinContent
            {
                LocationId = locationId,
                EntityType = "part",
                EntityId = run.PartId,
                Quantity = quantity,
                LotNumber = lotNumber,
                Status = BinContentStatus.Stored,
                PlacedBy = userId,
                PlacedAt = at,
            }, ct);
        }

        await inventory.AddMovementAsync(new BinMovement
        {
            EntityType = "part",
            EntityId = run.PartId,
            Quantity = quantity,
            LotNumber = lotNumber,
            ToLocationId = locationId,
            MovedBy = userId,
            MovedAt = at,
            Reason = BinMovementReason.Receive,
        }, ct);
    }

    /// <summary>The lot the run's output is stocked under: null unless the part is lot-tracked and CAP-INV-LOTS
    /// is on; then the lot record already made for this run, else a new lot record with the next house lot number
    /// carrying the good quantity and its barcode.</summary>
    private async Task<string?> ResolveRunLotAsync(ProductionRun run, decimal goodQty, CancellationToken ct)
    {
        if (!ReceiptLotRecords.TracksLot(run.Part, capabilities))
            return null;

        var existing = await db.LotRecords
            .Where(l => l.ProductionRunId == run.Id && l.PartId == run.PartId)
            .OrderBy(l => l.Id)
            .Select(l => l.LotNumber)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var lotNumber = await LotNumbers.NextAsync(db, clock, ct);

        var lot = new LotRecord
        {
            LotNumber = lotNumber,
            PartId = run.PartId,
            PartRevision = run.Job.PartRevision ?? run.Part.Revision,
            JobId = run.JobId,
            ProductionRunId = run.Id,
            Quantity = goodQty,
        };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync(ct);

        if (barcodes is not null)
            await barcodes.CreateBarcodeAsync(BarcodeEntityType.Lot, lot.Id, lot.LotNumber, ct);

        return lotNumber;
    }

    /// <summary>The bin to stock into: the requested bin when it is an active bin, else (with multi-location
    /// inventory on) the part's default bin when that is an active bin, else the default location that
    /// single-location stock screens read and write. Never an arbitrary first bin.</summary>
    private async Task<StorageLocation> ResolveReceivingBinAsync(
        IInventoryRepository inventory, int? requestedLocationId, int? partDefaultBinId, CancellationToken ct)
    {
        int?[] candidates = capabilities.IsEnabled(MultiLocationCapability)
            ? [requestedLocationId, partDefaultBinId]
            : [requestedLocationId];

        foreach (var candidate in candidates)
        {
            if (candidate is int id && await FindActiveBinAsync(id, ct) is { } bin)
                return bin;
        }

        return await inventory.EnsureDefaultLocationAsync(ct);
    }

    private Task<StorageLocation?> FindActiveBinAsync(int locationId, CancellationToken ct) =>
        db.StorageLocations.FirstOrDefaultAsync(l => l.Id == locationId
            && l.DeletedAt == null
            && l.IsActive
            && l.LocationType == LocationType.Bin, ct);

    private Task<ProductionRunResponseModel> BuildResponseAsync(ProductionRun run, CancellationToken ct) =>
        ProductionRunResponses.BuildAsync(db, run, ct);
}
