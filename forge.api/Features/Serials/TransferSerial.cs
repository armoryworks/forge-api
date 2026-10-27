using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
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
        RuleFor(x => x.Request.Notes).MaximumLength(1000).When(x => x.Request.Notes is not null);
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

        if (serial.Status is SerialNumberStatus.Shipped or SerialNumberStatus.Scrapped)
            throw new InvalidOperationException(
                $"Serial {serial.SerialValue} is {serial.Status.ToString().ToLowerInvariant()}, so it is no longer in stock to move.");
        if (serial.CurrentLocationId == request.Request.ToLocationId)
            throw new InvalidOperationException($"Serial {serial.SerialValue} is already in that location.");

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
