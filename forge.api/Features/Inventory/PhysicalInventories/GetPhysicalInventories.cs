using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record GetPhysicalInventoriesQuery(PhysicalInventoryStatus? Status) : IRequest<IReadOnlyList<PhysicalInventoryResponseModel>>;

public class GetPhysicalInventoriesHandler(IPhysicalInventoryService service) : IRequestHandler<GetPhysicalInventoriesQuery, IReadOnlyList<PhysicalInventoryResponseModel>>
{
    public Task<IReadOnlyList<PhysicalInventoryResponseModel>> Handle(GetPhysicalInventoriesQuery request, CancellationToken cancellationToken)
        => service.ListAsync(request.Status, cancellationToken);
}
