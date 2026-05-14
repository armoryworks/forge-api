using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record ExecuteScanMoveCommand(ScanMoveRequestModel Data) : IRequest<int>;

public class ExecuteScanMoveCommandValidator : AbstractValidator<ExecuteScanMoveCommand>
{
    public ExecuteScanMoveCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.FromLocationId).GreaterThan(0);
        RuleFor(x => x.Data.ToLocationId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.FromLocationId)
            .NotEqual(x => x.Data.ToLocationId)
            .WithMessage("Source and destination must be different");
    }
}

public class ExecuteScanMoveHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ExecuteScanMoveCommand, int>
{
    public async Task<int> Handle(ExecuteScanMoveCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        // Validate part exists
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == data.PartId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {data.PartId} not found");

        // Validate source has sufficient stock
        var sourceContent = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == data.PartId
                && bc.LocationId == data.FromLocationId
                && bc.Quantity > 0
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"No stock found for part {part.PartNumber} at source location");

        if (sourceContent.Quantity < data.Quantity)
            throw new InvalidOperationException(
                $"Cannot move {data.Quantity} — only {sourceContent.Quantity} available at source");

        // Validate destination exists
        var destExists = await db.StorageLocations.AsNoTracking()
            .AnyAsync(sl => sl.Id == data.ToLocationId, cancellationToken);
        if (!destExists)
            throw new KeyNotFoundException($"Destination location {data.ToLocationId} not found");

        // Decrement source
        sourceContent.Quantity -= data.Quantity;
        if (sourceContent.Quantity == 0)
        {
            sourceContent.RemovedAt = now;
            sourceContent.RemovedBy = userId;
        }

        // Increment or create destination bin content
        var destContent = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == data.PartId
                && bc.LocationId == data.ToLocationId
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (destContent != null)
        {
            destContent.Quantity += data.Quantity;
        }
        else
        {
            destContent = new BinContent
            {
                LocationId = data.ToLocationId,
                EntityType = "part",
                EntityId = data.PartId,
                Quantity = data.Quantity,
                LotNumber = sourceContent.LotNumber,
                PlacedBy = userId,
                PlacedAt = now,
            };
            db.BinContents.Add(destContent);
        }

        // Create scan action log
        var scanLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = ScanActionType.Move,
            PartId = data.PartId,
            PartNumber = part.PartNumber,
            FromLocationId = data.FromLocationId,
            ToLocationId = data.ToLocationId,
            Quantity = data.Quantity,
        };
        db.ScanActionLogs.Add(scanLog);
        await db.SaveChangesAsync(cancellationToken);

        // Create bin movement with scan log reference
        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = data.Quantity,
            LotNumber = sourceContent.LotNumber,
            FromLocationId = data.FromLocationId,
            ToLocationId = data.ToLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.ScanMove,
            ScanActionLogId = scanLog.Id,
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
