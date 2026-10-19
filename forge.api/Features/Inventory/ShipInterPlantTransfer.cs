using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ShipInterPlantTransferCommand(int Id, ShipTransferRequestModel Request) : IRequest;

public class ShipInterPlantTransferHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContext)
    : IRequestHandler<ShipInterPlantTransferCommand>
{
    public async Task Handle(ShipInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.InterPlantTransfers
            .Include(t => t.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {command.Id} not found");

        if (transfer.Status != InterPlantTransferStatus.Draft && transfer.Status != InterPlantTransferStatus.Approved)
            throw new InvalidOperationException($"Cannot ship transfer in {transfer.Status} status");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;
        var reference = InterPlantTransferStock.Reference(transfer);

        foreach (var line in transfer.Lines.OrderBy(l => l.Id))
        {
            var partNumber = line.Part?.PartNumber ?? $"part {line.PartId}";
            if (line.FromLocationId is not int fromLocationId)
                throw new InvalidOperationException($"Choose the bin {partNumber} ships from before shipping this transfer.");
            if (line.ToLocationId is null)
                throw new InvalidOperationException($"Choose the bin {partNumber} is received into before shipping this transfer.");

            var rows = (await ScanBinStock.DrawableRowsAsync(db, line.PartId, fromLocationId, line.Quantity, cancellationToken))
                .Where(r => line.LotNumber is null || r.LotNumber == line.LotNumber)
                .ToList();
            var free = rows.Sum(r => r.Quantity - r.ReservedQuantity);
            if (free < line.Quantity)
                throw new InvalidOperationException(
                    $"Cannot ship {line.Quantity} of {partNumber}: only {free} is free to move in the source bin.");

            foreach (var (row, taken) in BinContentDrawDown.Take(rows, line.Quantity, userId, now))
            {
                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = line.PartId,
                    Quantity = taken,
                    LotNumber = row.LotNumber,
                    FromLocationId = fromLocationId,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.Transfer,
                    Notes = reference,
                });
            }
        }

        transfer.Status = InterPlantTransferStatus.Shipped;
        transfer.ShippedAt = now;
        transfer.ShippedById = userId;
        transfer.TrackingNumber = command.Request.TrackingNumber;

        await db.SaveChangesAsync(cancellationToken);
    }
}
