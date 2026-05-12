using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Maintenance;

public record GetPredictiveMaintenanceDashboardQuery : IRequest<PredictiveMaintenanceDashboardResponseModel>;

public class GetPredictiveMaintenanceDashboardHandler(IPredictiveMaintenanceService predMaintService)
    : IRequestHandler<GetPredictiveMaintenanceDashboardQuery, PredictiveMaintenanceDashboardResponseModel>
{
    public async Task<PredictiveMaintenanceDashboardResponseModel> Handle(
        GetPredictiveMaintenanceDashboardQuery request, CancellationToken cancellationToken)
    {
        return await predMaintService.GetDashboardAsync(cancellationToken);
    }
}
