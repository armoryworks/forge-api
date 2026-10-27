using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record GetPhysicalInventoryVarianceQuery(int Id) : IRequest<PhysicalInventoryVarianceResponseModel>;

public class GetPhysicalInventoryVarianceHandler(IPhysicalInventoryService service) : IRequestHandler<GetPhysicalInventoryVarianceQuery, PhysicalInventoryVarianceResponseModel>
{
    public Task<PhysicalInventoryVarianceResponseModel> Handle(GetPhysicalInventoryVarianceQuery request, CancellationToken cancellationToken)
        => service.GetVarianceAsync(request.Id, cancellationToken);
}
