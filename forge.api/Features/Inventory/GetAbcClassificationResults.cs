using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetAbcClassificationResultsQuery(int RunId) : IRequest<List<AbcClassificationResultResponseModel>>;

public class GetAbcClassificationResultsHandler(AppDbContext db) : IRequestHandler<GetAbcClassificationResultsQuery, List<AbcClassificationResultResponseModel>>
{
    public async Task<List<AbcClassificationResultResponseModel>> Handle(GetAbcClassificationResultsQuery request, CancellationToken cancellationToken)
    {
        return await db.AbcClassifications
            .AsNoTracking()
            .Where(c => c.RunId == request.RunId)
            .Include(c => c.Part)
            .OrderBy(c => c.Rank)
            .Select(c => new AbcClassificationResultResponseModel
            {
                PartId = c.PartId,
                PartNumber = c.Part.PartNumber,
                PartDescription = c.Part.Description ?? c.Part.Name,
                Classification = c.Classification,
                AnnualUsageValue = c.AnnualUsageValue,
                AnnualDemandQuantity = c.AnnualDemandQuantity,
                UnitCost = c.UnitCost,
                CumulativePercent = c.CumulativePercent,
                Rank = c.Rank,
            })
            .ToListAsync(cancellationToken);
    }
}
