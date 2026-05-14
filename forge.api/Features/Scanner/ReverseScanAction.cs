using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
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
        switch (original.ActionType)
        {
            case ScanActionType.Move:
                await ReverseMoveAsync(original, userId, now, cancellationToken);
                break;
            case ScanActionType.CycleCount:
                // Cycle count reversals restore previous quantity — handled by creating opposite adjustment
                await ReverseCycleCountAsync(original, userId, now, cancellationToken);
                break;
            case ScanActionType.Receive:
                await ReverseReceiveAsync(original, userId, now, cancellationToken);
                break;
            case ScanActionType.Issue:
                await ReverseIssueAsync(original, userId, now, cancellationToken);
                break;
            case ScanActionType.Ship:
                await ReverseShipAsync(original, userId, now, cancellationToken);
                break;
            case ScanActionType.Return:
                await ReverseReturnAsync(original, userId, now, cancellationToken);
                break;
        }

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

    private async Task ReverseMoveAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        if (!original.FromLocationId.HasValue || !original.ToLocationId.HasValue || !original.PartId.HasValue)
            return;

        // Move stock back: TO → FROM
        await AdjustBinContentAsync(original.PartId.Value, original.ToLocationId.Value, -original.Quantity, userId, now, ct);
        await AdjustBinContentAsync(original.PartId.Value, original.FromLocationId.Value, original.Quantity, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = original.Quantity,
            FromLocationId = original.ToLocationId,
            ToLocationId = original.FromLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
        });
    }

    private async Task ReverseCycleCountAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Find the original movement to get the delta
        if (!original.PartId.HasValue || !original.FromLocationId.HasValue)
            return;

        var originalMovement = await db.BinMovements
            .Where(bm => bm.ScanActionLogId == original.Id)
            .FirstOrDefaultAsync(ct);

        if (originalMovement == null) return;

        // Reverse the delta
        var locationId = originalMovement.FromLocationId ?? originalMovement.ToLocationId;
        if (!locationId.HasValue) return;

        var delta = originalMovement.ToLocationId.HasValue
            ? -originalMovement.Quantity
            : originalMovement.Quantity;

        await AdjustBinContentAsync(original.PartId.Value, locationId.Value, delta, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = originalMovement.Quantity,
            FromLocationId = originalMovement.ToLocationId,
            ToLocationId = originalMovement.FromLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
            ReversedMovementId = originalMovement.Id,
        });
    }

    private async Task ReverseReceiveAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Remove stock from destination — does NOT undo PO receiving records
        if (!original.PartId.HasValue || !original.ToLocationId.HasValue)
            return;

        await AdjustBinContentAsync(original.PartId.Value, original.ToLocationId.Value, -original.Quantity, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = original.Quantity,
            FromLocationId = original.ToLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
        });
    }

    private async Task ReverseIssueAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Return stock to source location
        if (!original.PartId.HasValue || !original.FromLocationId.HasValue)
            return;

        await AdjustBinContentAsync(original.PartId.Value, original.FromLocationId.Value, original.Quantity, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = original.Quantity,
            ToLocationId = original.FromLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
        });
    }

    private async Task ReverseShipAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Return stock to source location
        if (!original.PartId.HasValue || !original.FromLocationId.HasValue)
            return;

        await AdjustBinContentAsync(original.PartId.Value, original.FromLocationId.Value, original.Quantity, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = original.Quantity,
            ToLocationId = original.FromLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
        });
    }

    private async Task ReverseReturnAsync(ScanActionLog original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        // Remove returned stock from destination
        if (!original.PartId.HasValue || !original.ToLocationId.HasValue)
            return;

        await AdjustBinContentAsync(original.PartId.Value, original.ToLocationId.Value, -original.Quantity, userId, now, ct);

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId.Value,
            Quantity = original.Quantity,
            FromLocationId = original.ToLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.Reversal,
        });
    }

    private async Task AdjustBinContentAsync(int partId, int locationId, decimal delta, int userId, DateTimeOffset now, CancellationToken ct)
    {
        var content = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LocationId == locationId
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(ct);

        if (delta > 0)
        {
            if (content != null)
            {
                content.Quantity += delta;
            }
            else
            {
                db.BinContents.Add(new BinContent
                {
                    LocationId = locationId,
                    EntityType = "part",
                    EntityId = partId,
                    Quantity = delta,
                    PlacedBy = userId,
                    PlacedAt = now,
                });
            }
        }
        else if (delta < 0 && content != null)
        {
            content.Quantity += delta; // delta is negative
            if (content.Quantity <= 0)
            {
                content.Quantity = 0;
                content.RemovedAt = now;
                content.RemovedBy = userId;
            }
        }
    }
}
