using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record PostPhysicalInventoryCommand(int Id, int UserId) : IRequest<PhysicalInventoryDetailResponseModel>;

public class PostPhysicalInventoryHandler(IPhysicalInventoryService service) : IRequestHandler<PostPhysicalInventoryCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(PostPhysicalInventoryCommand request, CancellationToken cancellationToken)
        => service.PostAsync(request.Id, request.UserId, cancellationToken);
}
