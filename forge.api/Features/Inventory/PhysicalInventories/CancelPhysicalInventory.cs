using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record CancelPhysicalInventoryCommand(int Id, int UserId) : IRequest<PhysicalInventoryDetailResponseModel>;

public class CancelPhysicalInventoryHandler(IPhysicalInventoryService service) : IRequestHandler<CancelPhysicalInventoryCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(CancelPhysicalInventoryCommand request, CancellationToken cancellationToken)
        => service.CancelAsync(request.Id, request.UserId, cancellationToken);
}
