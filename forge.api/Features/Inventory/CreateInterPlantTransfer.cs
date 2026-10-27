using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;

namespace Forge.Api.Features.Inventory;

public record CreateInterPlantTransferCommand(CreateInterPlantTransferRequestModel Request) : IRequest<InterPlantTransferResponseModel>;

public class CreateInterPlantTransferHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateInterPlantTransferCommand, InterPlantTransferResponseModel>
{
    private const string TransferNumberIndex = "ix_inter_plant_transfers_transfer_number";
    private const int NumberAttempts = 3;

    public async Task<InterPlantTransferResponseModel> Handle(CreateInterPlantTransferCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        if (request.FromPlantId == request.ToPlantId)
            throw new InvalidOperationException("Source and destination plants must be different");
        if (request.Lines.Count == 0)
            throw new InvalidOperationException("Add at least one part to the transfer.");

        _ = await db.Plants.FindAsync(new object[] { request.FromPlantId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Source plant {request.FromPlantId} not found");

        _ = await db.Plants.FindAsync(new object[] { request.ToPlantId }, cancellationToken)
            ?? throw new KeyNotFoundException($"Destination plant {request.ToPlantId} not found");

        var shopToday = ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken), clock.UtcNow);
        var numberPrefix = $"IPT-{shopToday:yyyyMMdd}";

        var transfer = new InterPlantTransfer
        {
            FromPlantId = request.FromPlantId,
            ToPlantId = request.ToPlantId,
            Notes = request.Notes,
        };

        foreach (var line in request.Lines)
        {
            var part = await db.Parts.FindAsync(new object[] { line.PartId }, cancellationToken)
                ?? throw new KeyNotFoundException($"Part {line.PartId} not found");
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Enter a quantity above zero for {part.PartNumber}.");

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
        for (var attempt = 1; ; attempt++)
        {
            transfer.TransferNumber = await DocumentNumberSequence.NextAsync(
                db.InterPlantTransfers.IgnoreQueryFilters().Select(t => t.TransferNumber), numberPrefix, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (attempt < NumberAttempts && ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TransferNumberIndex })
            {
            }
        }

        var created = await InterPlantTransferResponses.LoadAsync(
            db, db.InterPlantTransfers.Where(t => t.Id == transfer.Id), cancellationToken);
        return created[0];
    }
}
