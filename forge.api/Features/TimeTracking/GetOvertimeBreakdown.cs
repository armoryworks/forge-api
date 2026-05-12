using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record GetOvertimeBreakdownQuery(int UserId, DateOnly WeekOf) : IRequest<OvertimeBreakdownResponseModel>;

public class GetOvertimeBreakdownHandler(IOvertimeService overtimeService) : IRequestHandler<GetOvertimeBreakdownQuery, OvertimeBreakdownResponseModel>
{
    public async Task<OvertimeBreakdownResponseModel> Handle(GetOvertimeBreakdownQuery request, CancellationToken cancellationToken)
    {
        // Calculate the week start (Monday) and end (Sunday) from the given date
        var dayOfWeek = request.WeekOf.DayOfWeek;
        var daysToMonday = dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
        var weekStart = request.WeekOf.AddDays(-daysToMonday);
        var weekEnd = weekStart.AddDays(6);

        return await overtimeService.CalculateOvertimeAsync(request.UserId, weekStart, weekEnd, cancellationToken);
    }
}
