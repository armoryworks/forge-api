using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetLocationTreeQuery : IRequest<List<StorageLocationResponseModel>>;

public class GetLocationTreeHandler(IInventoryRepository repo) : IRequestHandler<GetLocationTreeQuery, List<StorageLocationResponseModel>>
{
    public Task<List<StorageLocationResponseModel>> Handle(GetLocationTreeQuery request, CancellationToken cancellationToken)
        => repo.GetLocationTreeAsync(cancellationToken);
}
