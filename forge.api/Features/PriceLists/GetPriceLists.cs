using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record GetPriceListsQuery(int? CustomerId) : IRequest<List<PriceListListItemModel>>;

public class GetPriceListsHandler(IPriceListRepository repo)
    : IRequestHandler<GetPriceListsQuery, List<PriceListListItemModel>>
{
    public async Task<List<PriceListListItemModel>> Handle(GetPriceListsQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAllAsync(request.CustomerId, cancellationToken);
    }
}
