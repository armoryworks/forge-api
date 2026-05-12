using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Serials;

public record CreateSerialNumberCommand(int PartId, CreateSerialNumberRequestModel Request) : IRequest<SerialNumberResponseModel>;

public class CreateSerialNumberValidator : AbstractValidator<CreateSerialNumberCommand>
{
    public CreateSerialNumberValidator()
    {
        RuleFor(x => x.Request.SerialValue).NotEmpty().MaximumLength(100);
    }
}

public class CreateSerialNumberHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateSerialNumberCommand, SerialNumberResponseModel>
{
    public async Task<SerialNumberResponseModel> Handle(CreateSerialNumberCommand request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        // Pre-beta: replaced legacy `IsSerialTracked` boolean with the
        // TraceabilityType enum. Only the Serial flavour permits creating
        // SerialNumber rows; Lot-only and untraced parts route elsewhere.
        if (part.TraceabilityType != TraceabilityType.Serial)
            throw new InvalidOperationException($"Part {part.PartNumber} is not configured for serial tracking");

        var exists = await db.SerialNumbers.AnyAsync(s => s.SerialValue == request.Request.SerialValue, cancellationToken);
        if (exists)
            throw new InvalidOperationException($"Serial number '{request.Request.SerialValue}' already exists");

        var serial = new SerialNumber
        {
            PartId = request.PartId,
            SerialValue = request.Request.SerialValue,
            Status = SerialNumberStatus.Available,
            JobId = request.Request.JobId,
            LotRecordId = request.Request.LotRecordId,
            CurrentLocationId = request.Request.CurrentLocationId,
            ParentSerialId = request.Request.ParentSerialId,
            Notes = request.Request.Notes,
            ManufacturedAt = clock.UtcNow,
        };

        db.SerialNumbers.Add(serial);

        var history = new SerialHistory
        {
            SerialNumber = serial,
            Action = "Created",
            ToLocationName = request.Request.CurrentLocationId.HasValue
                ? await db.StorageLocations.Where(l => l.Id == request.Request.CurrentLocationId).Select(l => l.Name).FirstOrDefaultAsync(cancellationToken)
                : null,
            Details = $"Serial number created for part {part.PartNumber}",
            OccurredAt = clock.UtcNow,
        };

        db.Set<SerialHistory>().Add(history);
        await db.SaveChangesAsync(cancellationToken);

        return new SerialNumberResponseModel(
            serial.Id,
            serial.PartId,
            part.PartNumber,
            serial.SerialValue,
            serial.Status,
            serial.JobId,
            null,
            serial.LotRecordId,
            null,
            serial.CurrentLocationId,
            history.ToLocationName,
            serial.ShipmentLineId,
            serial.CustomerId,
            null,
            serial.ParentSerialId,
            null,
            serial.ManufacturedAt,
            serial.ShippedAt,
            serial.ScrappedAt,
            serial.Notes,
            serial.CreatedAt,
            0);
    }
}
