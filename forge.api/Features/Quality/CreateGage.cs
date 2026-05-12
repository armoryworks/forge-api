using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateGageCommand(CreateGageRequestModel Request) : IRequest<GageResponseModel>;

public class CreateGageValidator : AbstractValidator<CreateGageCommand>
{
    public CreateGageValidator()
    {
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Request.CalibrationIntervalDays).GreaterThan(0);
    }
}

public class CreateGageHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateGageCommand, GageResponseModel>
{
    public async Task<GageResponseModel> Handle(CreateGageCommand request, CancellationToken cancellationToken)
    {
        var nextNumber = await GetNextGageNumberAsync(cancellationToken);

        var gage = new Gage
        {
            GageNumber = nextNumber,
            Description = request.Request.Description.Trim(),
            GageType = request.Request.GageType?.Trim(),
            Manufacturer = request.Request.Manufacturer?.Trim(),
            Model = request.Request.Model?.Trim(),
            SerialNumber = request.Request.SerialNumber?.Trim(),
            CalibrationIntervalDays = request.Request.CalibrationIntervalDays,
            Status = GageStatus.InService,
            LocationId = request.Request.LocationId,
            AssetId = request.Request.AssetId,
            AccuracySpec = request.Request.AccuracySpec?.Trim(),
            RangeSpec = request.Request.RangeSpec?.Trim(),
            Resolution = request.Request.Resolution?.Trim(),
            Notes = request.Request.Notes?.Trim(),
            NextCalibrationDue = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddDays(request.Request.CalibrationIntervalDays)),
        };

        db.Gages.Add(gage);
        await db.SaveChangesAsync(cancellationToken);

        return new GageResponseModel(
            gage.Id, gage.GageNumber, gage.Description, gage.GageType, gage.Manufacturer,
            gage.Model, gage.SerialNumber, gage.CalibrationIntervalDays, gage.LastCalibratedAt,
            gage.NextCalibrationDue, gage.Status, gage.LocationId, null, gage.AssetId, null,
            gage.AccuracySpec, gage.RangeSpec, gage.Resolution, gage.Notes, gage.CreatedAt, 0);
    }

    private async Task<string> GetNextGageNumberAsync(CancellationToken cancellationToken)
    {
        var lastNumber = await db.Gages.AsNoTracking()
            .OrderByDescending(g => g.GageNumber)
            .Select(g => g.GageNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastNumber == null) return "GAG-00001";

        var parts = lastNumber.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[1], out var num))
            return $"GAG-{(num + 1):D5}";

        return $"GAG-00001";
    }
}
