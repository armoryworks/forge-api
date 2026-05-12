using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateGageCommand(int Id, UpdateGageRequestModel Request) : IRequest<GageResponseModel>;

public class UpdateGageHandler(AppDbContext db) : IRequestHandler<UpdateGageCommand, GageResponseModel>
{
    public async Task<GageResponseModel> Handle(UpdateGageCommand request, CancellationToken cancellationToken)
    {
        var gage = await db.Gages
            .Include(g => g.Location)
            .Include(g => g.Asset)
            .Include(g => g.CalibrationRecords)
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Gage {request.Id} not found");

        var r = request.Request;
        if (r.Description != null) gage.Description = r.Description.Trim();
        if (r.GageType != null) gage.GageType = r.GageType.Trim();
        if (r.Manufacturer != null) gage.Manufacturer = r.Manufacturer.Trim();
        if (r.Model != null) gage.Model = r.Model.Trim();
        if (r.SerialNumber != null) gage.SerialNumber = r.SerialNumber.Trim();
        if (r.CalibrationIntervalDays.HasValue) gage.CalibrationIntervalDays = r.CalibrationIntervalDays.Value;
        if (r.Status.HasValue) gage.Status = r.Status.Value;
        if (r.LocationId.HasValue) gage.LocationId = r.LocationId;
        if (r.AssetId.HasValue) gage.AssetId = r.AssetId;
        if (r.AccuracySpec != null) gage.AccuracySpec = r.AccuracySpec.Trim();
        if (r.RangeSpec != null) gage.RangeSpec = r.RangeSpec.Trim();
        if (r.Resolution != null) gage.Resolution = r.Resolution.Trim();
        if (r.Notes != null) gage.Notes = r.Notes.Trim();

        await db.SaveChangesAsync(cancellationToken);

        return new GageResponseModel(
            gage.Id, gage.GageNumber, gage.Description, gage.GageType, gage.Manufacturer,
            gage.Model, gage.SerialNumber, gage.CalibrationIntervalDays, gage.LastCalibratedAt,
            gage.NextCalibrationDue, gage.Status, gage.LocationId, gage.Location?.Name,
            gage.AssetId, gage.Asset?.Name, gage.AccuracySpec, gage.RangeSpec, gage.Resolution,
            gage.Notes, gage.CreatedAt, gage.CalibrationRecords.Count);
    }
}
