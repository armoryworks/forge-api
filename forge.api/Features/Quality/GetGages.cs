using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetGagesQuery(GageStatus? Status, string? Search) : IRequest<List<GageResponseModel>>;

public class GetGagesHandler(AppDbContext db) : IRequestHandler<GetGagesQuery, List<GageResponseModel>>
{
    public async Task<List<GageResponseModel>> Handle(GetGagesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Gages.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(g => g.Status == request.Status.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(g =>
                g.GageNumber.ToLower().Contains(s) ||
                g.Description.ToLower().Contains(s) ||
                (g.SerialNumber != null && g.SerialNumber.ToLower().Contains(s)));
        }

        return await query
            .OrderBy(g => g.GageNumber)
            .Select(g => new GageResponseModel(
                g.Id,
                g.GageNumber,
                g.Description,
                g.GageType,
                g.Manufacturer,
                g.Model,
                g.SerialNumber,
                g.CalibrationIntervalDays,
                g.LastCalibratedAt,
                g.NextCalibrationDue,
                g.Status,
                g.LocationId,
                g.Location != null ? g.Location.Name : null,
                g.AssetId,
                g.Asset != null ? g.Asset.Name : null,
                g.AccuracySpec,
                g.RangeSpec,
                g.Resolution,
                g.Notes,
                g.CreatedAt,
                g.CalibrationRecords.Count))
            .ToListAsync(cancellationToken);
    }
}
