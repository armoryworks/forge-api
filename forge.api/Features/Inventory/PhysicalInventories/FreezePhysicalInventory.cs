using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record FreezePhysicalInventoryCommand(int Id, int UserId) : IRequest<PhysicalInventoryDetailResponseModel>;

public class FreezePhysicalInventoryHandler(IPhysicalInventoryService service) : IRequestHandler<FreezePhysicalInventoryCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(FreezePhysicalInventoryCommand request, CancellationToken cancellationToken)
        => service.FreezeAsync(request.Id, request.UserId, cancellationToken);
}
