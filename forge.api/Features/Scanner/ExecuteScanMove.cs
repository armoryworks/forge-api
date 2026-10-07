using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
        var sourceRows = await ScanBinStock.ActiveRowsAsync(db, data.PartId, data.FromLocationId, cancellationToken);
        if (!sourceRows.Any(r => r.Quantity > 0))
            throw new KeyNotFoundException($"No stock found for part {part.PartNumber} at source location");

        var available = sourceRows.Sum(r => r.Quantity - r.ReservedQuantity);
        if (available < data.Quantity)
            throw new InvalidOperationException(
                $"Cannot move {data.Quantity} — only {available} available at source");

        // Validate destination exists
        var destExists = await db.StorageLocations.AsNoTracking()
            .AnyAsync(sl => sl.Id == data.ToLocationId, cancellationToken);
        if (!destExists)
            throw new KeyNotFoundException($"Destination location {data.ToLocationId} not found");

        var drawn = BinContentDrawDown.Take(sourceRows, data.Quantity, userId, now);
        foreach (var (row, taken) in drawn)
            await ScanBinStock.AddAsync(db, data.PartId, data.ToLocationId, row.LotNumber, taken, userId, now, cancellationToken);

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

        foreach (var (row, taken) in drawn)
        {
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = data.PartId,
                Quantity = taken,
                LotNumber = row.LotNumber,
                FromLocationId = data.FromLocationId,
                ToLocationId = data.ToLocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.ScanMove,
                ScanActionLogId = scanLog.Id,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
