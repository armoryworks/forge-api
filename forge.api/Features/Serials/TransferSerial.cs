using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Serials;

public record TransferSerialCommand(int SerialId, TransferSerialRequestModel Request) : IRequest;

public class TransferSerialValidator : AbstractValidator<TransferSerialCommand>
{
    public TransferSerialValidator()
    {
        RuleFor(x => x.Request.ToLocationId).GreaterThan(0);
    }
}

public class TransferSerialHandler(AppDbContext db, IClock clock) : IRequestHandler<TransferSerialCommand>
{
    public async Task Handle(TransferSerialCommand request, CancellationToken cancellationToken)
    {
        var serial = await db.SerialNumbers
            .Include(s => s.CurrentLocation)
            .FirstOrDefaultAsync(s => s.Id == request.SerialId, cancellationToken)
            ?? throw new KeyNotFoundException($"Serial number {request.SerialId} not found");

        var toLocation = await db.StorageLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.Request.ToLocationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Location {request.Request.ToLocationId} not found");

        var fromLocationName = serial.CurrentLocation?.Name;

        var history = new SerialHistory
        {
            SerialNumberId = serial.Id,
            Action = "Transferred",
            FromLocationName = fromLocationName,
            ToLocationName = toLocation.Name,
            Details = request.Request.Notes,
            OccurredAt = clock.UtcNow,
        };

        serial.CurrentLocationId = request.Request.ToLocationId;

        db.Set<SerialHistory>().Add(history);
        await db.SaveChangesAsync(cancellationToken);
    }
}
