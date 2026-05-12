using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.SalesOrders;

public record GetPendingDropShipsQuery : IRequest<IReadOnlyList<DropShipStatusResponseModel>>;

public class GetPendingDropShipsHandler(IDropShipService dropShipService) : IRequestHandler<GetPendingDropShipsQuery, IReadOnlyList<DropShipStatusResponseModel>>
{
    public Task<IReadOnlyList<DropShipStatusResponseModel>> Handle(GetPendingDropShipsQuery request, CancellationToken cancellationToken)
        => dropShipService.GetPendingDropShipsAsync(cancellationToken);
}
