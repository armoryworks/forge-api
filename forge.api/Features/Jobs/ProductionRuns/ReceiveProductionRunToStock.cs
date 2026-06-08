using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// Receive a completed production run's good output into finished-goods stock — the explicit job-complete→FG
/// step (separate from flipping the run to Completed). Stocks the FG bin operationally and, when
/// CAP-ACCT-FULLGL is on, posts Dr INVENTORY_FG / Cr INVENTORY_WIP at standard cost + feeds the FG valuation
/// store. Idempotent: a run already received to stock is a no-op (returns current state).
/// </summary>
public record ReceiveProductionRunToStockCommand(int JobId, int RunId, int ReceivedByUserId)
    : IRequest<ProductionRunResponseModel>;

public class ReceiveProductionRunToStockHandler(
    AppDbContext db,
    // Operational FG stock-in (creates/increments BinContent + a Receive movement). Null in mock-based unit
    // tests → no stock movement, run is still stamped received.
    IInventoryRepository? inventory = null,
    // Phase-2 STAGE E — inline FG/WIP GL posting; null/default keeps the handler constructible without an
    // accounting context, and it no-ops while CAP-ACCT-FULLGL is off.
    IProductionReceiptPostingService? posting = null)
    : IRequestHandler<ReceiveProductionRunToStockCommand, ProductionRunResponseModel>
{
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

            // Operational FG stock-in (not CAP-ACCT-FULLGL gated): find-or-create the active BinContent for
            // (part, FG bin) and increment it, then record a Receive movement.
            if (inventory is not null)
            {
                var locationId = await ResolveFinishedGoodsBinAsync(inventory, cancellationToken);

                var existing = await inventory.FindActiveBinContentByPartLocationAsync(
                    run.PartId, locationId, cancellationToken);
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
                        PlacedAt = DateTimeOffset.UtcNow,
                    }, cancellationToken);
                }

                await inventory.AddMovementAsync(new BinMovement
                {
                    EntityType = "part",
                    EntityId = run.PartId,
                    Quantity = goodQty,
                    ToLocationId = locationId,
                    MovedBy = request.ReceivedByUserId,
                    MovedAt = DateTimeOffset.UtcNow,
                    Reason = BinMovementReason.Receive,
                }, cancellationToken);
            }

            run.ReceivedQuantity = goodQty;
            run.ReceivedToStockAt = DateTimeOffset.UtcNow;

            // One transaction: the received stamp AND the inline FG/WIP posting commit (or roll back) together.
            // Npgsql opens a real transaction; the in-memory test provider treats it as a no-op. tx is opened
            // only when posting is wired.
            await using var tx = posting is not null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

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

    /// <summary>The finished-goods bin to stock into: the first active bin, or a freshly provisioned "Finished
    /// Goods" bin if the warehouse has none yet (so a first production receipt always has somewhere to land).</summary>
    private static async Task<int> ResolveFinishedGoodsBinAsync(IInventoryRepository inventory, CancellationToken ct)
    {
        var bins = await inventory.GetBinLocationsAsync(ct);
        if (bins.Count > 0)
            return bins[0].Id;

        var fg = new StorageLocation { Name = "Finished Goods", LocationType = LocationType.Bin, IsActive = true };
        await inventory.AddLocationAsync(fg, ct);
        return fg.Id;
    }

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
