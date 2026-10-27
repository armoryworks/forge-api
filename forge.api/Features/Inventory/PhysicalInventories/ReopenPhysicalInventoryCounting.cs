using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record ReopenPhysicalInventoryCountingCommand(int Id) : IRequest<PhysicalInventoryDetailResponseModel>;

public class ReopenPhysicalInventoryCountingHandler(IPhysicalInventoryService service) : IRequestHandler<ReopenPhysicalInventoryCountingCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(ReopenPhysicalInventoryCountingCommand request, CancellationToken cancellationToken)
        => service.ReopenCountingAsync(request.Id, cancellationToken);
}
