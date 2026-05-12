using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetPartInventoryQuery(string? Search) : IRequest<List<InventoryPartSummaryResponseModel>>;

public class GetPartInventoryHandler(IInventoryRepository repo) : IRequestHandler<GetPartInventoryQuery, List<InventoryPartSummaryResponseModel>>
{
    public Task<List<InventoryPartSummaryResponseModel>> Handle(GetPartInventoryQuery request, CancellationToken cancellationToken)
        => repo.GetPartInventorySummaryAsync(request.Search, cancellationToken);
}
