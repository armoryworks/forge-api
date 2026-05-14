using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record GetAndonBoardDataQuery : IRequest<List<AndonBoardWorkCenterResponseModel>>;

public class GetAndonBoardDataHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<GetAndonBoardDataQuery, List<AndonBoardWorkCenterResponseModel>>
{
    public async Task<List<AndonBoardWorkCenterResponseModel>> Handle(
        GetAndonBoardDataQuery request, CancellationToken cancellationToken)
    {
        var workCenters = await db.WorkCenters
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);

        var activeAlerts = await mediator.Send(new GetAndonAlertsQuery(null, AndonAlertStatus.Active), cancellationToken);
        var acknowledgedAlerts = await mediator.Send(new GetAndonAlertsQuery(null, AndonAlertStatus.Acknowledged), cancellationToken);

        var allActiveAlerts = activeAlerts.Concat(acknowledgedAlerts).ToList();
        var alertsByWorkCenter = allActiveAlerts.ToLookup(a => a.WorkCenterId);

        return workCenters.Select(wc =>
        {
            var wcAlerts = alertsByWorkCenter[wc.Id].ToList();

            var status = wcAlerts.Any(a => a.Type == AndonAlertType.Safety || a.Status == AndonAlertStatus.Active)
                ? "Red"
                : wcAlerts.Any() ? "Yellow" : "Green";

            return new AndonBoardWorkCenterResponseModel
            {
                WorkCenterId = wc.Id,
                WorkCenterName = wc.Name,
                Status = status,
                ActiveAlerts = wcAlerts,
            };
        }).ToList();
    }
}
