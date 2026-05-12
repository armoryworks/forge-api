using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders;

public record GetPendingBackToBacksQuery : IRequest<IReadOnlyList<BackToBackStatusResponseModel>>;

public class GetPendingBackToBacksHandler(IBackToBackService backToBackService) : IRequestHandler<GetPendingBackToBacksQuery, IReadOnlyList<BackToBackStatusResponseModel>>
{
    public async Task<IReadOnlyList<BackToBackStatusResponseModel>> Handle(GetPendingBackToBacksQuery query, CancellationToken cancellationToken)
    {
        return await backToBackService.GetPendingBackToBacksAsync(cancellationToken);
    }
}
