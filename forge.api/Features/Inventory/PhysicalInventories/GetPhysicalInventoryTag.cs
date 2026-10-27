using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record GetPhysicalInventoryTagQuery(int Id, int TagNumber) : IRequest<PhysicalInventoryTagResponseModel>;

public class GetPhysicalInventoryTagHandler(IPhysicalInventoryService service) : IRequestHandler<GetPhysicalInventoryTagQuery, PhysicalInventoryTagResponseModel>
{
    public Task<PhysicalInventoryTagResponseModel> Handle(GetPhysicalInventoryTagQuery request, CancellationToken cancellationToken)
        => service.FindTagAsync(request.Id, request.TagNumber, cancellationToken);
}
