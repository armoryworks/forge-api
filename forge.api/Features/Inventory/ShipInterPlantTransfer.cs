using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ShipInterPlantTransferCommand(int Id, ShipTransferRequestModel Request) : IRequest;

public class ShipInterPlantTransferHandler(AppDbContext db, IClock clock) : IRequestHandler<ShipInterPlantTransferCommand>
{
    public async Task Handle(ShipInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.InterPlantTransfers
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {command.Id} not found");

        if (transfer.Status != InterPlantTransferStatus.Draft && transfer.Status != InterPlantTransferStatus.Approved)
            throw new InvalidOperationException($"Cannot ship transfer in {transfer.Status} status");

        transfer.Status = InterPlantTransferStatus.Shipped;
        transfer.ShippedAt = clock.UtcNow;
        transfer.TrackingNumber = command.Request.TrackingNumber;

        await db.SaveChangesAsync(cancellationToken);
    }
}
