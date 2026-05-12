using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateSpcCharacteristicCommand(CreateSpcCharacteristicRequestModel Data) : IRequest<SpcCharacteristicResponseModel>;

public class CreateSpcCharacteristicCommandValidator : AbstractValidator<CreateSpcCharacteristicCommand>
{
    public CreateSpcCharacteristicCommandValidator()
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

public class CreateSpcCharacteristicHandler(AppDbContext db)
    : IRequestHandler<CreateSpcCharacteristicCommand, SpcCharacteristicResponseModel>
{
    public async Task<SpcCharacteristicResponseModel> Handle(
        CreateSpcCharacteristicCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;

        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == data.PartId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {data.PartId} not found.");

        string? operationName = null;
        if (data.OperationId.HasValue)
        {
            operationName = await db.Operations.AsNoTracking()
                .Where(o => o.Id == data.OperationId.Value)
                .Select(o => o.Title)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Operation {data.OperationId} not found.");
        }

        var characteristic = new SpcCharacteristic
        {
            PartId = data.PartId,
            OperationId = data.OperationId,
            Name = data.Name.Trim(),
            Description = data.Description?.Trim(),
            MeasurementType = data.MeasurementType,
            NominalValue = data.NominalValue,
            UpperSpecLimit = data.UpperSpecLimit,
            LowerSpecLimit = data.LowerSpecLimit,
            UnitOfMeasure = data.UnitOfMeasure?.Trim(),
            DecimalPlaces = data.DecimalPlaces,
            SampleSize = data.SampleSize,
            SampleFrequency = data.SampleFrequency?.Trim(),
            GageId = data.GageId,
            NotifyOnOoc = data.NotifyOnOoc,
        };

        db.SpcCharacteristics.Add(characteristic);
        await db.SaveChangesAsync(cancellationToken);

        return new SpcCharacteristicResponseModel
        {
            Id = characteristic.Id,
            PartId = characteristic.PartId,
            PartNumber = part.PartNumber,
            OperationId = characteristic.OperationId,
            OperationName = operationName,
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
            MeasurementCount = 0,
            LatestCpk = null,
        };
    }
}
