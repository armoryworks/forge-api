using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetGageByIdQuery(int Id) : IRequest<GageResponseModel>;

public class GetGageByIdHandler(AppDbContext db) : IRequestHandler<GetGageByIdQuery, GageResponseModel>
{
    public async Task<GageResponseModel> Handle(GetGageByIdQuery request, CancellationToken cancellationToken)
    {
        var g = await db.Gages.AsNoTracking()
            .Include(x => x.Location)
            .Include(x => x.Asset)
            .Include(x => x.CalibrationRecords)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Gage {request.Id} not found");

        return new GageResponseModel(
            g.Id, g.GageNumber, g.Description, g.GageType, g.Manufacturer, g.Model,
            g.SerialNumber, g.CalibrationIntervalDays, g.LastCalibratedAt, g.NextCalibrationDue,
            g.Status, g.LocationId, g.Location?.Name, g.AssetId, g.Asset?.Name,
            g.AccuracySpec, g.RangeSpec, g.Resolution, g.Notes, g.CreatedAt,
            g.CalibrationRecords.Count);
    }
}
