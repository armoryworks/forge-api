using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public record GetAssetMaintenanceLogsQuery(int AssetId) : IRequest<List<MaintenanceLogListItemResponseModel>>;

public class GetAssetMaintenanceLogsHandler(AppDbContext db)
    : IRequestHandler<GetAssetMaintenanceLogsQuery, List<MaintenanceLogListItemResponseModel>>
{
    public async Task<List<MaintenanceLogListItemResponseModel>> Handle(
        GetAssetMaintenanceLogsQuery request, CancellationToken ct)
    {
        return await db.MaintenanceLogs
            .Include(l => l.Schedule)
            .Where(l => l.Schedule.AssetId == request.AssetId && l.Schedule.DeletedAt == null)
            .OrderByDescending(l => l.PerformedAt)
            .Take(10)
            .Select(l => new MaintenanceLogListItemResponseModel(
                l.Id,
                l.Schedule.Title,
                l.PerformedAt,
                db.Users
                    .Where(u => u.Id == l.PerformedById)
                    .Select(u => u.LastName + ", " + u.FirstName)
                    .FirstOrDefault() ?? "Unknown",
                l.HoursAtService,
                l.Notes,
                l.Cost))
            .ToListAsync(ct);
    }
}
