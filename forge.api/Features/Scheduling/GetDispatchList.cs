using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Scheduling;

public record GetDispatchListQuery(int WorkCenterId) : IRequest<IReadOnlyList<DispatchListItemModel>>;

public class GetDispatchListHandler(ISchedulingService schedulingService) : IRequestHandler<GetDispatchListQuery, IReadOnlyList<DispatchListItemModel>>
{
    public async Task<IReadOnlyList<DispatchListItemModel>> Handle(GetDispatchListQuery request, CancellationToken cancellationToken)
    {
        return await schedulingService.GetDispatchListAsync(request.WorkCenterId, cancellationToken);
    }
}
