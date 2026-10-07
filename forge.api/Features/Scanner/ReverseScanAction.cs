using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
    IHttpContextAccessor httpContext)
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
        {
            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null!, currentUser.PinHash, data.Pin);
            pinValid = result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }

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

        // Create counter-movements based on action type
        await ReverseStockAsync(original, userId, now, cancellationToken);

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
    }

    private async Task ReverseStockAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        if (!original.PartId.HasValue)
            return;
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
                return;
            movements.Add((null, null, original.Quantity, original.FromLocationId, original.ToLocationId));
        }

        foreach (var (movementId, lotNumber, movedQuantity, fromLocationId, toLocationId) in movements)
        {
            var quantity = Math.Abs(movedQuantity);
            if (toLocationId is int to)
                await ScanBinStock.RemoveAsync(db, partId, to, lotNumber, quantity, userId, now, ct);
            if (fromLocationId is int from)
                await ScanBinStock.AddAsync(db, partId, from, lotNumber, quantity, userId, now, ct);

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
    }
}
