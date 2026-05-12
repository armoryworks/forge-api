using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Maintenance;

public record GetModelPerformanceQuery(string ModelId) : IRequest<MlModelPerformanceResponseModel>;

public class GetModelPerformanceHandler(IPredictiveMaintenanceService predMaintService)
    : IRequestHandler<GetModelPerformanceQuery, MlModelPerformanceResponseModel>
{
    public async Task<MlModelPerformanceResponseModel> Handle(
        GetModelPerformanceQuery request, CancellationToken cancellationToken)
    {
        return await predMaintService.GetModelPerformanceAsync(request.ModelId, cancellationToken);
    }
}
