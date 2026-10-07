using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ReceiveInterPlantTransferCommand(int Id, List<ReceiveTransferLineRequestModel> Lines) : IRequest;

public class ReceiveInterPlantTransferHandler(AppDbContext db, IClock clock) : IRequestHandler<ReceiveInterPlantTransferCommand>
{
    public async Task Handle(ReceiveInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.InterPlantTransfers
            .Include(t => t.Lines)
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
            if (receivedLinesByPart.TryGetValue(line.PartId, out var qty))
                line.ReceivedQuantity = qty;
        }

        transfer.Status = InterPlantTransferStatus.Received;
        transfer.ReceivedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
