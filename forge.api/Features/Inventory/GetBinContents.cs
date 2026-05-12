using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetBinContentsQuery(int LocationId) : IRequest<List<BinContentResponseModel>>;

public class GetBinContentsHandler(IInventoryRepository repo) : IRequestHandler<GetBinContentsQuery, List<BinContentResponseModel>>
{
    public Task<List<BinContentResponseModel>> Handle(GetBinContentsQuery request, CancellationToken cancellationToken)
        => repo.GetBinContentsAsync(request.LocationId, cancellationToken);
}
