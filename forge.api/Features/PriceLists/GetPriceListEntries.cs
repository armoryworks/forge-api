using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record GetPriceListEntriesQuery(
    int PriceListId,
    string? Search,
    int Page,
    int PageSize) : IRequest<PagedResponse<PriceListEntryResponseModel>>;

public class GetPriceListEntriesHandler(IPriceListRepository repo)
    : IRequestHandler<GetPriceListEntriesQuery, PagedResponse<PriceListEntryResponseModel>>
{
    public async Task<PagedResponse<PriceListEntryResponseModel>> Handle(
        GetPriceListEntriesQuery request, CancellationToken cancellationToken)
    {
        if (!await repo.PriceListExistsAsync(request.PriceListId, cancellationToken))
            throw new KeyNotFoundException($"Price list {request.PriceListId} not found");

        return await repo.GetEntriesAsync(
            request.PriceListId, request.Search, request.Page, request.PageSize, cancellationToken);
    }
}
