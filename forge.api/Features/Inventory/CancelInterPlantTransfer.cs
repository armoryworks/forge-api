using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Inventory;

public record CancelInterPlantTransferCommand(int Id) : IRequest;

public class CancelInterPlantTransferHandler(AppDbContext db) : IRequestHandler<CancelInterPlantTransferCommand>
{
    public async Task Handle(CancelInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.InterPlantTransfers
            .FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {command.Id} not found");

        var blockedReason = transfer.Status switch
        {
            InterPlantTransferStatus.Draft or InterPlantTransferStatus.Approved => null,
            InterPlantTransferStatus.Cancelled => "This transfer is already cancelled.",
            InterPlantTransferStatus.Received => "This transfer has already been received.",
            _ => "This transfer has shipped, so it can no longer be cancelled. Receive it at the destination plant instead.",
        };
        if (blockedReason is not null)
            throw new InvalidOperationException(blockedReason);

        transfer.Status = InterPlantTransferStatus.Cancelled;
        db.LogActivityAt("cancelled", $"Inter-plant transfer {transfer.TransferNumber} cancelled", ("InterPlantTransfer", transfer.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
