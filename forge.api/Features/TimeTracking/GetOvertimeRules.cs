using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record GetOvertimeRulesQuery : IRequest<List<OvertimeRuleResponseModel>>;

public class GetOvertimeRulesHandler(AppDbContext db) : IRequestHandler<GetOvertimeRulesQuery, List<OvertimeRuleResponseModel>>
{
    public async Task<List<OvertimeRuleResponseModel>> Handle(GetOvertimeRulesQuery request, CancellationToken cancellationToken)
    {
        return await db.Set<Core.Entities.OvertimeRule>().AsNoTracking()
            .Where(r => r.DeletedAt == null)
            .OrderByDescending(r => r.IsDefault)
            .ThenBy(r => r.Name)
            .Select(r => new OvertimeRuleResponseModel(
                r.Id, r.Name,
                r.DailyThresholdHours, r.WeeklyThresholdHours,
                r.OvertimeMultiplier,
                r.DoubletimeThresholdDailyHours, r.DoubletimeThresholdWeeklyHours,
                r.DoubletimeMultiplier,
                r.IsDefault, r.ApplyDailyBeforeWeekly))
            .ToListAsync(cancellationToken);
    }
}
