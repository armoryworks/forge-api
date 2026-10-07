using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// Receive a completed production run's good output into finished-goods stock — the explicit job-complete→FG
/// step (separate from flipping the run to Completed). Stocks the chosen bin operationally and, when
/// CAP-ACCT-FULLGL is on, posts Dr INVENTORY_FG / Cr INVENTORY_WIP at standard cost + feeds the FG valuation
/// store. Idempotent: a run already received to stock is a no-op (returns current state).
/// </summary>
public record ReceiveProductionRunToStockCommand(int JobId, int RunId, int ReceivedByUserId, int? LocationId = null)
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
    IProductionReceiptPostingService? posting = null)
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
            await using var tx = posting is not null && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            // Operational FG stock-in (not CAP-ACCT-FULLGL gated): find-or-create the active un-lotted BinContent for
            // (part, bin) and increment it, then record a Receive movement.
            string? binName = null;
            if (inventory is not null)
            {
                var location = await ResolveReceivingBinAsync(
                    inventory, request.LocationId, run.Part.DefaultBinId, cancellationToken);
                var locationId = location.Id;
                binName = location.Name;

                var existing = await inventory.FindActiveBinContentByPartLocationLotAsync(
                    run.PartId, locationId, null, cancellationToken);
                if (existing is not null)
                {
                    existing.Quantity += goodQty;
                }
                else
                {
                    await inventory.AddBinContentAsync(new BinContent
                    {
                        LocationId = locationId,
                        EntityType = "part",
                        EntityId = run.PartId,
                        Quantity = goodQty,
                        Status = BinContentStatus.Stored,
                        PlacedBy = request.ReceivedByUserId,
                        PlacedAt = receivedAt,
                    }, cancellationToken);
                }

                await inventory.AddMovementAsync(new BinMovement
                {
                    EntityType = "part",
                    EntityId = run.PartId,
                    Quantity = goodQty,
                    ToLocationId = locationId,
                    MovedBy = request.ReceivedByUserId,
                    MovedAt = receivedAt,
                    Reason = BinMovementReason.Receive,
                }, cancellationToken);
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
                    : $"Received {goodQty} of {run.Part.PartNumber} from run {run.RunNumber} into {binName}.",
                CreatedAt = receivedAt,
            });

            await db.SaveChangesAsync(cancellationToken);

            if (posting is not null)
            {
                var entryDate = DateOnly.FromDateTime(run.ReceivedToStockAt.Value.UtcDateTime);
                await posting.PostProductionReceiptAsync(
                    run.Id, entryDate, request.ReceivedByUserId, cancellationToken);
            }

            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
        }

        return await BuildResponseAsync(run, cancellationToken);
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

    private async Task<ProductionRunResponseModel> BuildResponseAsync(ProductionRun run, CancellationToken ct)
    {
        string? operatorName = null;
        if (run.OperatorId.HasValue)
        {
            var user = await db.Users.FindAsync([run.OperatorId.Value], ct);
            if (user is not null)
                operatorName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var yieldPct = ProductionRun.YieldPercent(run.CompletedQuantity, run.ScrapQuantity);

        return new ProductionRunResponseModel(
            run.Id,
            run.JobId,
            run.Job.JobNumber,
            run.PartId,
            run.Part.PartNumber,
            run.Part.Description ?? run.Part.Name,
            run.OperatorId,
            operatorName,
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
            yieldPct,
            run.ReceivedQuantity,
            run.ReceivedToStockAt);
    }
}
