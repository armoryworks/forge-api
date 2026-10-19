using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ReceiveInterPlantTransferCommand(int Id, List<ReceiveTransferLineRequestModel> Lines) : IRequest;

public class ReceiveInterPlantTransferHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext,
    IInventoryAdjustmentPostingService? posting = null)
    : IRequestHandler<ReceiveInterPlantTransferCommand>
{
    public async Task Handle(ReceiveInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.InterPlantTransfers
            .Include(t => t.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {command.Id} not found");

        var blockedReason = transfer.Status switch
        {
            InterPlantTransferStatus.Shipped or InterPlantTransferStatus.InTransit => null,
            InterPlantTransferStatus.Received => "This transfer has already been received.",
            InterPlantTransferStatus.Cancelled => "This transfer was cancelled.",
            _ => "This transfer can be received once it has shipped.",
        };
        if (blockedReason is not null)
            throw new InvalidOperationException(blockedReason);

        var receivedLinesByPart = command.Lines.ToDictionary(l => l.PartId, l => l.ReceivedQuantity);
        foreach (var line in transfer.Lines)
        {
            if (receivedLinesByPart.TryGetValue(line.PartId, out var qty) && (qty < 0 || qty > line.Quantity))
                throw new InvalidOperationException(
                    $"Receive between 0 and {line.Quantity} of {line.Part?.PartNumber ?? $"part {line.PartId}"}.");
        }

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;
        var reference = InterPlantTransferStock.Reference(transfer);
        var shortages = new List<InventoryAdjustmentPosting>();

        var shippedMovements = await db.BinMovements.AsNoTracking()
            .Where(m => m.Notes == reference
                && m.Reason == BinMovementReason.Transfer
                && m.FromLocationId != null
                && m.ToLocationId == null)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);

        foreach (var line in transfer.Lines.OrderBy(l => l.Id))
        {
            var received = receivedLinesByPart.TryGetValue(line.PartId, out var qty) ? qty : line.Quantity;
            line.ReceivedQuantity = received;

            var shippedLots = shippedMovements
                .Where(m => m.EntityId == line.PartId && m.FromLocationId == line.FromLocationId)
                .GroupBy(m => m.LotNumber)
                .Select(g => (LotNumber: g.Key, Quantity: g.Sum(m => m.Quantity)))
                .ToList();
            var shipped = shippedLots.Sum(l => l.Quantity);
            if (shipped <= 0)
                continue;

            if (line.ToLocationId is not int toLocationId)
                throw new InvalidOperationException(
                    $"Choose the bin {line.Part?.PartNumber ?? $"part {line.PartId}"} is received into.");

            var remaining = Math.Min(received, shipped);
            foreach (var (lotNumber, lotQuantity) in shippedLots)
            {
                if (remaining <= 0)
                    break;
                var placed = Math.Min(lotQuantity, remaining);
                remaining -= placed;

                await ScanBinStock.AddAsync(db, line.PartId, toLocationId, lotNumber, placed, userId, now, cancellationToken);
                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = line.PartId,
                    Quantity = placed,
                    LotNumber = lotNumber,
                    ToLocationId = toLocationId,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.Transfer,
                    Notes = reference,
                });
            }

            var shortage = shipped - Math.Min(received, shipped);
            if (shortage > 0)
            {
                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = line.PartId,
                    Quantity = shortage,
                    LotNumber = line.LotNumber,
                    MovedBy = userId,
                    MovedAt = now,
                    Reason = BinMovementReason.Adjustment,
                    Notes = $"{reference}: {shortage} short on receipt",
                });
                shortages.Add(new InventoryAdjustmentPosting(
                    "InterPlantTransferLine", line.Id, line.PartId, -shortage,
                    DateOnly.FromDateTime(now.UtcDateTime), userId, $"transfer {transfer.TransferNumber}"));
            }
        }

        transfer.Status = InterPlantTransferStatus.Received;
        transfer.ReceivedAt = now;
        transfer.ReceivedById = userId;

        await using var tx = posting is not null && shortages.Count > 0 && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);

        if (posting is not null)
        {
            foreach (var shortage in shortages)
                await posting.PostAdjustmentAsync(shortage, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
