using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateSpcCharacteristicCommand(int Id, UpdateSpcCharacteristicRequestModel Data) : IRequest<SpcCharacteristicResponseModel>;

public class UpdateSpcCharacteristicCommandValidator : AbstractValidator<UpdateSpcCharacteristicCommand>
{
    public UpdateSpcCharacteristicCommandValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Data.Description).MaximumLength(1000);
        RuleFor(x => x.Data.UpperSpecLimit).GreaterThan(x => x.Data.NominalValue)
            .WithMessage("Upper spec limit must be greater than nominal value.");
        RuleFor(x => x.Data.LowerSpecLimit).LessThan(x => x.Data.NominalValue)
            .WithMessage("Lower spec limit must be less than nominal value.");
        RuleFor(x => x.Data.SampleSize).InclusiveBetween(2, 25);
        RuleFor(x => x.Data.DecimalPlaces).InclusiveBetween(0, 6);
        RuleFor(x => x.Data.UnitOfMeasure).MaximumLength(50);
        RuleFor(x => x.Data.SampleFrequency).MaximumLength(200);
    }
}

public class UpdateSpcCharacteristicHandler(AppDbContext db, ISpcService spcService)
    : IRequestHandler<UpdateSpcCharacteristicCommand, SpcCharacteristicResponseModel>
{
    public async Task<SpcCharacteristicResponseModel> Handle(
        UpdateSpcCharacteristicCommand request, CancellationToken cancellationToken)
    {
        var characteristic = await db.SpcCharacteristics
            .Include(c => c.Part)
            .Include(c => c.Operation)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"SPC Characteristic {request.Id} not found.");

        var data = request.Data;
        var specLimitsChanged = characteristic.UpperSpecLimit != data.UpperSpecLimit
            || characteristic.LowerSpecLimit != data.LowerSpecLimit
            || characteristic.NominalValue != data.NominalValue;

        characteristic.Name = data.Name.Trim();
        characteristic.Description = data.Description?.Trim();
        characteristic.MeasurementType = data.MeasurementType;
        characteristic.NominalValue = data.NominalValue;
        characteristic.UpperSpecLimit = data.UpperSpecLimit;
        characteristic.LowerSpecLimit = data.LowerSpecLimit;
        characteristic.UnitOfMeasure = data.UnitOfMeasure?.Trim();
        characteristic.DecimalPlaces = data.DecimalPlaces;
        characteristic.SampleSize = data.SampleSize;
        characteristic.SampleFrequency = data.SampleFrequency?.Trim();
        characteristic.GageId = data.GageId;
        characteristic.IsActive = data.IsActive;
        characteristic.NotifyOnOoc = data.NotifyOnOoc;

        await db.SaveChangesAsync(cancellationToken);

        // Recalculate control limits if spec limits changed
        if (specLimitsChanged)
        {
            var measurementCount = await db.SpcMeasurements
                .CountAsync(m => m.CharacteristicId == request.Id, cancellationToken);

            if (measurementCount >= 2)
                await spcService.CalculateControlLimitsAsync(request.Id, null, null, cancellationToken);
        }

        var latestCpk = await db.SpcControlLimits.AsNoTracking()
            .Where(cl => cl.CharacteristicId == request.Id && cl.IsActive)
            .Select(cl => (decimal?)cl.Cpk)
            .FirstOrDefaultAsync(cancellationToken);

        var measurementTotal = await db.SpcMeasurements
            .CountAsync(m => m.CharacteristicId == request.Id, cancellationToken);

        return new SpcCharacteristicResponseModel
        {
            Id = characteristic.Id,
            PartId = characteristic.PartId,
            PartNumber = characteristic.Part.PartNumber,
            OperationId = characteristic.OperationId,
            OperationName = characteristic.Operation?.Title,
            Name = characteristic.Name,
            Description = characteristic.Description,
            MeasurementType = characteristic.MeasurementType.ToString(),
            NominalValue = characteristic.NominalValue,
            UpperSpecLimit = characteristic.UpperSpecLimit,
            LowerSpecLimit = characteristic.LowerSpecLimit,
            UnitOfMeasure = characteristic.UnitOfMeasure,
            DecimalPlaces = characteristic.DecimalPlaces,
            SampleSize = characteristic.SampleSize,
            SampleFrequency = characteristic.SampleFrequency,
            GageId = characteristic.GageId,
            IsActive = characteristic.IsActive,
            NotifyOnOoc = characteristic.NotifyOnOoc,
            MeasurementCount = measurementTotal,
            LatestCpk = latestCpk,
        };
    }
}
