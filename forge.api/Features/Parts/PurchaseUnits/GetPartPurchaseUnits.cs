using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts.PurchaseUnits;

// UoM purchase-units effort — list a part's purchase units (sizes/forms).
public record GetPartPurchaseUnitsQuery(int PartId) : IRequest<List<PartPurchaseUnitResponseModel>>;

public class GetPartPurchaseUnitsHandler(AppDbContext db)
    : IRequestHandler<GetPartPurchaseUnitsQuery, List<PartPurchaseUnitResponseModel>>
{
    public Task<List<PartPurchaseUnitResponseModel>> Handle(GetPartPurchaseUnitsQuery request, CancellationToken ct)
        => db.PartPurchaseUnits
            .AsNoTracking()
            .Where(o => o.PartId == request.PartId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new PartPurchaseUnitResponseModel(
                o.Id,
                o.PartId,
                o.Label,
                o.ContentQuantity,
                o.ContentUomId,
                o.ContentUom != null ? o.ContentUom.Code : null,
                o.ContentUom != null ? o.ContentUom.Name : null,
                o.SortOrder,
                o.IsActive))
            .ToListAsync(ct);
}
