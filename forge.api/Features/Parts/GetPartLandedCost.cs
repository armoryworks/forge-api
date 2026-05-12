using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Bought-parts effort PR3 — landed-cost surface for the part Cost tab.
/// Returns a "door-to-door" average over the most recent N receipts plus
/// per-receipt detail and a vendor comparison. See
/// <see cref="IPartLandedCostService"/> for the calc; this query is just
/// the MediatR shell.
/// </summary>
public record GetPartLandedCostQuery(int PartId, int MaxReceipts = 3)
    : IRequest<PartLandedCostResponseModel>;

public class GetPartLandedCostHandler(IPartLandedCostService service)
    : IRequestHandler<GetPartLandedCostQuery, PartLandedCostResponseModel>
{
    public Task<PartLandedCostResponseModel> Handle(GetPartLandedCostQuery request, CancellationToken ct)
        => service.GetForPartAsync(request.PartId, request.MaxReceipts, ct);
}
