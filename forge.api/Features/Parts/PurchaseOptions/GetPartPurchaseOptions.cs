using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts.PurchaseOptions;

// UoM purchase-options effort — list a part's purchase options (sizes/forms).
public record GetPartPurchaseOptionsQuery(int PartId) : IRequest<List<PartPurchaseOptionResponseModel>>;

public class GetPartPurchaseOptionsHandler(AppDbContext db)
    : IRequestHandler<GetPartPurchaseOptionsQuery, List<PartPurchaseOptionResponseModel>>
{
    public Task<List<PartPurchaseOptionResponseModel>> Handle(GetPartPurchaseOptionsQuery request, CancellationToken ct)
        => db.PartPurchaseOptions
            .AsNoTracking()
            .Where(o => o.PartId == request.PartId)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new PartPurchaseOptionResponseModel(
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
