using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetUnitsOfMeasureQuery(string? Category = null) : IRequest<List<UomResponseModel>>;

public class GetUnitsOfMeasureHandler(AppDbContext db)
    : IRequestHandler<GetUnitsOfMeasureQuery, List<UomResponseModel>>
{
    public async Task<List<UomResponseModel>> Handle(GetUnitsOfMeasureQuery request, CancellationToken ct)
    {
        var query = db.UnitsOfMeasure.AsNoTracking().Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(u => u.Category.ToString() == request.Category);

        return await query
            .OrderBy(u => u.Category)
            .ThenBy(u => u.SortOrder)
            .Select(u => new UomResponseModel(
                u.Id, u.Code, u.Name, u.Symbol,
                u.Category.ToString(), u.DecimalPlaces, u.IsBaseUnit, u.IsActive, u.SortOrder))
            .ToListAsync(ct);
    }
}
