using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetReservationsQuery(int? PartId, int? JobId) : IRequest<List<ReservationResponseModel>>;

public class GetReservationsHandler(IInventoryRepository repo) : IRequestHandler<GetReservationsQuery, List<ReservationResponseModel>>
{
    public Task<List<ReservationResponseModel>> Handle(GetReservationsQuery request, CancellationToken ct)
        => repo.GetReservationsAsync(request.PartId, request.JobId, ct);
}
