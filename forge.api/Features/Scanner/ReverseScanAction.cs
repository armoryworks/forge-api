using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Auth;
using Forge.Api.Features.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record ReverseScanActionCommand(ScanReversalRequestModel Data) : IRequest;

public class ReverseScanActionCommandValidator : AbstractValidator<ReverseScanActionCommand>
{
    public ReverseScanActionCommandValidator()
    {
        RuleFor(x => x.Data.ScanActionLogId).GreaterThan(0);
        RuleFor(x => x.Data.Pin).NotEmpty().WithMessage("PIN is required for reversals");
    }
}

public class ReverseScanActionHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext,
    IMediator mediator,
    IInventoryAdjustmentPostingService? posting = null)
    : IRequestHandler<ReverseScanActionCommand>
{
    public async Task Handle(ReverseScanActionCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        // Find original scan action
        var original = await db.ScanActionLogs
            .Where(sal => sal.Id == data.ScanActionLogId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Scan action log {data.ScanActionLogId} not found");

        if (original.IsReversed)
            throw new InvalidOperationException("This scan action has already been reversed");

        // Only certain action types can be reversed via scan
        if (original.ActionType is ScanActionType.Inspect or ScanActionType.JobStart
            or ScanActionType.JobStop or ScanActionType.JobAdvance)
            throw new InvalidOperationException($"Cannot reverse {original.ActionType} actions via scanner");

        // Validate PIN — check if the current user's PIN matches, or if they're a manager/admin
        var currentUser = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.PinHash })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Current user not found");

        var pinValid = false;

        // Check current user's PIN
        if (!string.IsNullOrEmpty(currentUser.PinHash))
            pinValid = SetPinHandler.VerifyPin(data.Pin, currentUser.PinHash);

        // If current user's PIN doesn't match and they're not the original user, check if they're a manager/admin
        if (!pinValid)
        {
            var isManagerOrAdmin = await db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles.Where(r => r.Name == "Admin" || r.Name == "Manager"),
                    ur => ur.RoleId, r => r.Id, (_, _) => true)
                .AnyAsync(cancellationToken);

            if (!isManagerOrAdmin)
                throw new InvalidOperationException("Invalid PIN or insufficient permissions for reversal");

            // Manager/admin can use their own PIN even if original user differs
            // PIN was already checked above; if still not valid, reject
            if (!pinValid)
                throw new InvalidOperationException("Invalid PIN");
        }

        var issueIds = await ScannedIssueIdsAsync(original, cancellationToken);
        if (issueIds.Count > 0
            && await MaterialIssueReturn.AnyReturnedAsync(db, original.RelatedEntityId!.Value, issueIds, cancellationToken))
            throw new InvalidOperationException(
                "This material was already returned from the job, so the scan cannot be reversed.");

        // Create counter-movements based on action type
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var stockChange = await ReverseStockAsync(original, userId, now, cancellationToken);
        foreach (var issueId in issueIds)
            await mediator.Send(new ReturnMaterialIssueCommand(
                original.RelatedEntityId!.Value, issueId, userId, StockRestoredByCaller: true), cancellationToken);

        // Mark original as reversed
        original.IsReversed = true;

        // Create reversal scan log
        var reversalLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = original.ActionType,
            PartId = original.PartId,
            PartNumber = original.PartNumber,
            FromLocationId = original.ToLocationId,
            ToLocationId = original.FromLocationId,
            Quantity = original.Quantity,
            ReversesLogId = original.Id,
        };
        db.ScanActionLogs.Add(reversalLog);
        await db.SaveChangesAsync(cancellationToken);

        // Update original with reverse reference
        original.ReversedByLogId = reversalLog.Id;
        await db.SaveChangesAsync(cancellationToken);

        if (posting is not null && original.ActionType == ScanActionType.CycleCount && stockChange != 0m)
            await posting.PostAdjustmentAsync(new InventoryAdjustmentPosting(
                "ScanCountReversal", original.Id, original.PartId!.Value, stockChange,
                DateOnly.FromDateTime(now.UtcDateTime), userId, $"scanner count {original.Id} reversed"), cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    private async Task<List<int>> ScannedIssueIdsAsync(ScanActionLog original, CancellationToken ct)
    {
        if (original.ActionType != ScanActionType.Issue || original.RelatedEntityId is not int jobId)
            return [];

        var note = ScanIssueNote.For(original.Id);
        return await db.MaterialIssues.AsNoTracking()
            .Where(mi => mi.JobId == jobId && mi.IssueType == MaterialIssueType.Issue && mi.Notes == note)
            .OrderBy(mi => mi.Id)
            .Select(mi => mi.Id)
            .ToListAsync(ct);
    }

    private async Task<decimal> ReverseStockAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        if (!original.PartId.HasValue)
            return 0m;
        var partId = original.PartId.Value;

        var recorded = await db.BinMovements.AsNoTracking()
            .Where(bm => bm.ScanActionLogId == original.Id)
            .OrderBy(bm => bm.Id)
            .Select(bm => new { bm.Id, bm.LotNumber, bm.Quantity, bm.FromLocationId, bm.ToLocationId })
            .ToListAsync(ct);
        var movements = recorded
            .Select(bm => ((int?)bm.Id, bm.LotNumber, bm.Quantity, bm.FromLocationId, bm.ToLocationId))
            .ToList();

        if (movements.Count == 0)
        {
            if (original.ActionType == ScanActionType.CycleCount)
                return 0m;
            movements.Add((null, null, original.Quantity, original.FromLocationId, original.ToLocationId));
        }

        var change = 0m;
        foreach (var (movementId, lotNumber, movedQuantity, fromLocationId, toLocationId) in movements)
        {
            var quantity = Math.Abs(movedQuantity);
            if (toLocationId is int to)
            {
                await ScanBinStock.RemoveAsync(
                    db, partId, to, lotNumber, anyLot: movementId is null, quantity, userId, now, ct);
                change -= quantity;
            }
            if (fromLocationId is int from)
            {
                await ScanBinStock.AddAsync(db, partId, from, lotNumber, quantity, userId, now, ct);
                change += quantity;
            }

            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = partId,
                Quantity = quantity,
                LotNumber = lotNumber,
                FromLocationId = toLocationId,
                ToLocationId = fromLocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.Reversal,
                ReversedMovementId = movementId,
            });
        }

        return change;
    }
}
