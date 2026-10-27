using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory.PhysicalInventories;

public record SubmitPhysicalInventoryForReviewCommand(int Id) : IRequest<PhysicalInventoryDetailResponseModel>;

public class SubmitPhysicalInventoryForReviewHandler(IPhysicalInventoryService service) : IRequestHandler<SubmitPhysicalInventoryForReviewCommand, PhysicalInventoryDetailResponseModel>
{
    public Task<PhysicalInventoryDetailResponseModel> Handle(SubmitPhysicalInventoryForReviewCommand request, CancellationToken cancellationToken)
        => service.SubmitForReviewAsync(request.Id, cancellationToken);
}
