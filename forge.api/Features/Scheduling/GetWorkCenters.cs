using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record GetWorkCentersQuery() : IRequest<List<WorkCenterResponseModel>>;

public class GetWorkCentersHandler(AppDbContext db) : IRequestHandler<GetWorkCentersQuery, List<WorkCenterResponseModel>>
{
    public async Task<List<WorkCenterResponseModel>> Handle(GetWorkCentersQuery request, CancellationToken cancellationToken)
    {
        return await db.WorkCenters
            .AsNoTracking()
            .Include(w => w.Asset)
            .Include(w => w.Location)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Name)
            .Select(w => new WorkCenterResponseModel(
                w.Id, w.Name, w.Code, w.Description,
                w.DailyCapacityHours, w.EfficiencyPercent,
                w.NumberOfMachines, w.LaborCostPerHour,
                w.BurdenRatePerHour, w.IsActive,
                w.AssetId, w.Asset != null ? w.Asset.Name : null,
                w.CompanyLocationId, w.Location != null ? w.Location.Name : null,
                w.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
