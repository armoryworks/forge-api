using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetAbcClassificationRunsQuery : IRequest<List<AbcClassificationRunResponseModel>>;

public class GetAbcClassificationRunsHandler(AppDbContext db) : IRequestHandler<GetAbcClassificationRunsQuery, List<AbcClassificationRunResponseModel>>
{
    public async Task<List<AbcClassificationRunResponseModel>> Handle(GetAbcClassificationRunsQuery request, CancellationToken cancellationToken)
    {
        return await db.AbcClassificationRuns
            .AsNoTracking()
            .OrderByDescending(r => r.RunDate)
            .Select(r => new AbcClassificationRunResponseModel
            {
                Id = r.Id,
                RunDate = r.RunDate,
                TotalParts = r.TotalParts,
                ClassACount = r.ClassACount,
                ClassBCount = r.ClassBCount,
                ClassCCount = r.ClassCCount,
                ClassAThresholdPercent = r.ClassAThresholdPercent,
                ClassBThresholdPercent = r.ClassBThresholdPercent,
                TotalAnnualUsageValue = r.TotalAnnualUsageValue,
                LookbackMonths = r.LookbackMonths,
            })
            .ToListAsync(cancellationToken);
    }
}
