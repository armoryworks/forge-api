using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record CreateInterPlantTransferCommand(CreateInterPlantTransferRequestModel Request) : IRequest<InterPlantTransferResponseModel>;

public class CreateInterPlantTransferHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateInterPlantTransferCommand, InterPlantTransferResponseModel>
{
    public async Task<InterPlantTransferResponseModel> Handle(CreateInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (request.FromPlantId == request.ToPlantId)
            throw new InvalidOperationException("Source and destination plants must be different");

        var fromPlant = await db.Plants.FindAsync(new object[] { request.FromPlantId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Source plant {request.FromPlantId} not found");

        var toPlant = await db.Plants.FindAsync(new object[] { request.ToPlantId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Destination plant {request.ToPlantId} not found");

        var now = clock.UtcNow;
        var transferNumber = $"IPT-{now:yyyyMMdd}-{now:HHmmss}";

        var transfer = new InterPlantTransfer
        {
            TransferNumber = transferNumber,
            FromPlantId = request.FromPlantId,
            ToPlantId = request.ToPlantId,
            Notes = request.Notes,
        };

        foreach (var line in request.Lines)
        {
            var part = await db.Parts.FindAsync(new object[] { line.PartId }, cancellationToken)
                ?? throw new KeyNotFoundException($"Part {line.PartId} not found");

            transfer.Lines.Add(new InterPlantTransferLine
            {
                PartId = line.PartId,
                Quantity = line.Quantity,
                FromLocationId = line.FromLocationId,
                ToLocationId = line.ToLocationId,
                LotNumber = line.LotNumber,
            });
        }

        db.InterPlantTransfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);

        // Reload with nav properties
        await db.Entry(transfer).Reference(t => t.FromPlant).LoadAsync(cancellationToken);
        await db.Entry(transfer).Reference(t => t.ToPlant).LoadAsync(cancellationToken);

        return new InterPlantTransferResponseModel(
            transfer.Id, transfer.TransferNumber,
            transfer.FromPlantId, fromPlant.Name,
            transfer.ToPlantId, toPlant.Name,
            transfer.Status, transfer.ShippedAt, transfer.ReceivedAt,
            transfer.TrackingNumber, transfer.Notes,
            transfer.Lines.Count,
            transfer.Lines.Select(l => new InterPlantTransferLineResponseModel(
                l.Id, l.PartId, "", "", l.Quantity, l.ReceivedQuantity, l.LotNumber)).ToList(),
            transfer.CreatedAt, transfer.UpdatedAt);
    }
}
