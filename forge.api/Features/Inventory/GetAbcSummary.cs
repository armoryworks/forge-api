using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record GetAbcSummaryQuery : IRequest<AbcClassificationSummaryResponseModel>;

public class GetAbcSummaryHandler(AppDbContext db) : IRequestHandler<GetAbcSummaryQuery, AbcClassificationSummaryResponseModel>
{
    public async Task<AbcClassificationSummaryResponseModel> Handle(GetAbcSummaryQuery request, CancellationToken cancellationToken)
    {
        var latestRun = await db.AbcClassificationRuns
            .AsNoTracking()
            .OrderByDescending(r => r.RunDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRun is null)
        {
            return new AbcClassificationSummaryResponseModel();
        }

        var classifications = await db.AbcClassifications
            .AsNoTracking()
            .Where(c => c.RunId == latestRun.Id)
            .ToListAsync(cancellationToken);

        var totalValue = classifications.Sum(c => c.AnnualUsageValue);
        var classAValue = classifications.Where(c => c.Classification == AbcClass.A).Sum(c => c.AnnualUsageValue);
        var classBValue = classifications.Where(c => c.Classification == AbcClass.B).Sum(c => c.AnnualUsageValue);
        var classCValue = classifications.Where(c => c.Classification == AbcClass.C).Sum(c => c.AnnualUsageValue);

        return new AbcClassificationSummaryResponseModel
        {
            LastRunDate = latestRun.RunDate,
            ClassACount = latestRun.ClassACount,
            ClassBCount = latestRun.ClassBCount,
            ClassCCount = latestRun.ClassCCount,
            ClassAValuePercent = totalValue > 0 ? classAValue / totalValue * 100 : 0,
            ClassBValuePercent = totalValue > 0 ? classBValue / totalValue * 100 : 0,
            ClassCValuePercent = totalValue > 0 ? classCValue / totalValue * 100 : 0,
        };
    }
}
