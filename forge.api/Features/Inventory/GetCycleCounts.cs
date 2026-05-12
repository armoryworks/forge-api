using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetCycleCountsQuery(int? LocationId, string? Status) : IRequest<List<CycleCountResponseModel>>;

public class GetCycleCountsHandler(IInventoryRepository repo)
    : IRequestHandler<GetCycleCountsQuery, List<CycleCountResponseModel>>
{
    public Task<List<CycleCountResponseModel>> Handle(
        GetCycleCountsQuery request, CancellationToken cancellationToken)
        => repo.GetCycleCountsAsync(request.LocationId, request.Status, cancellationToken);
}
