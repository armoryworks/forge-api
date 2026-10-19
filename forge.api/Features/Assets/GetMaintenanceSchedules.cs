using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record GetMaintenanceSchedulesQuery(int? AssetId) : IRequest<List<MaintenanceScheduleResponseModel>>;

public class GetMaintenanceSchedulesHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetMaintenanceSchedulesQuery, List<MaintenanceScheduleResponseModel>>
{
    public async Task<List<MaintenanceScheduleResponseModel>> Handle(
        GetMaintenanceSchedulesQuery request, CancellationToken ct)
    {
        var query = db.MaintenanceSchedules
            .Include(s => s.Asset)
            .Where(s => s.IsActive);

        if (request.AssetId.HasValue)
            query = query.Where(s => s.AssetId == request.AssetId.Value);

        var now = clock.UtcNow;

        var schedules = await query
            .AsNoTracking()
            .OrderBy(s => s.NextDueAt)
            .ToListAsync(ct);

        var lastService = await MaintenanceUsage.LastServiceReadingsAsync(db, schedules.Select(s => s.Id).ToList(), ct);

        return schedules
            .Select(s => new MaintenanceScheduleResponseModel(
                s.Id,
                s.AssetId,
                s.Asset.Name,
                s.Title,
                s.Description,
                s.IntervalDays,
                s.IntervalHours,
                s.IntervalShots,
                s.LastPerformedAt,
                s.NextDueAt,
                s.IsActive,
                MaintenanceUsage.IsDue(s, s.Asset, lastService.GetValueOrDefault(s.Id), now)))
            .ToList();
    }
}
