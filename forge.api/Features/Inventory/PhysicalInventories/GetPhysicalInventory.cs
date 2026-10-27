using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record GetPhysicalInventoryQuery(int Id) : IRequest<PhysicalInventoryDetailResponseModel>;

public class GetPhysicalInventoryHandler(IPhysicalInventoryService service) : IRequestHandler<GetPhysicalInventoryQuery, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(GetPhysicalInventoryQuery request, CancellationToken cancellationToken)
        => service.GetAsync(request.Id, cancellationToken);
}
