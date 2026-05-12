using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Scheduling;

public record GetWorkCenterLoadQuery(int WorkCenterId, DateOnly From, DateOnly To) : IRequest<WorkCenterLoadResponseModel>;

public class GetWorkCenterLoadHandler(ISchedulingService schedulingService) : IRequestHandler<GetWorkCenterLoadQuery, WorkCenterLoadResponseModel>
{
    public async Task<WorkCenterLoadResponseModel> Handle(GetWorkCenterLoadQuery request, CancellationToken cancellationToken)
    {
        return await schedulingService.GetWorkCenterLoadAsync(request.WorkCenterId, request.From, request.To, cancellationToken);
    }
}
