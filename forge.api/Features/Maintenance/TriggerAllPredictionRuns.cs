using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

/// <summary>Runs predictive maintenance for every active work center (the daily job and the predictions page's
/// Run now action). Returns how many work centers were checked.</summary>
public record TriggerAllPredictionRunsCommand : IRequest<int>;

public class TriggerAllPredictionRunsHandler(AppDbContext db, IPredictiveMaintenanceService predMaintService)
    : IRequestHandler<TriggerAllPredictionRunsCommand, int>
{
    public async Task<int> Handle(TriggerAllPredictionRunsCommand command, CancellationToken cancellationToken)
    {
        var workCenterIds = await db.WorkCenters.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        foreach (var workCenterId in workCenterIds)
            await predMaintService.TriggerPredictionRunAsync(workCenterId, cancellationToken);

        return workCenterIds.Count;
    }
}
