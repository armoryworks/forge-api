using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.Quality;

public record GetGagesDueQuery(int DaysAhead = 30) : IRequest<List<GageResponseModel>>;

public class GetGagesDueHandler(AppDbContext db, IClock clock) : IRequestHandler<GetGagesDueQuery, List<GageResponseModel>>
{
    public async Task<List<GageResponseModel>> Handle(GetGagesDueQuery request, CancellationToken cancellationToken)
    {
        var cutoff = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddDays(request.DaysAhead));

        return await db.Gages.AsNoTracking()
            .Where(g => g.Status != GageStatus.Retired &&
                        g.NextCalibrationDue.HasValue &&
                        g.NextCalibrationDue.Value <= cutoff)
            .OrderBy(g => g.NextCalibrationDue)
            .Select(g => new GageResponseModel(
                g.Id, g.GageNumber, g.Description, g.GageType, g.Manufacturer, g.Model,
                g.SerialNumber, g.CalibrationIntervalDays, g.LastCalibratedAt, g.NextCalibrationDue,
                g.Status, g.LocationId, g.Location != null ? g.Location.Name : null,
                g.AssetId, g.Asset != null ? g.Asset.Name : null,
                g.AccuracySpec, g.RangeSpec, g.Resolution, g.Notes, g.CreatedAt,
                g.CalibrationRecords.Count))
            .ToListAsync(cancellationToken);
    }
}
