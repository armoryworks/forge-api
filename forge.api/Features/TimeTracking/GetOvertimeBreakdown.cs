using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record GetOvertimeBreakdownQuery(int UserId, DateOnly? WeekOf) : IRequest<OvertimeBreakdownResponseModel>;

public class GetOvertimeBreakdownQueryValidator : AbstractValidator<GetOvertimeBreakdownQuery>
{
    public GetOvertimeBreakdownQueryValidator()
    {
        RuleFor(x => x.WeekOf!.Value)
            .InclusiveBetween(new DateOnly(1900, 1, 1), new DateOnly(9999, 12, 1))
            .OverridePropertyName(nameof(GetOvertimeBreakdownQuery.WeekOf))
            .When(x => x.WeekOf.HasValue);
    }
}

public class GetOvertimeBreakdownHandler(AppDbContext db, IOvertimeService overtimeService, IClock clock) : IRequestHandler<GetOvertimeBreakdownQuery, OvertimeBreakdownResponseModel>
{
    public async Task<OvertimeBreakdownResponseModel> Handle(GetOvertimeBreakdownQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken))
            throw new KeyNotFoundException($"User {request.UserId} not found.");

        var weekOf = request.WeekOf
            ?? DateOnly.FromDateTime(ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken), clock.UtcNow));

        // Calculate the week start (Monday) and end (Sunday) from the given date
        var dayOfWeek = weekOf.DayOfWeek;
        var daysToMonday = dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
        var weekStart = weekOf.AddDays(-daysToMonday);
        var weekEnd = weekStart.AddDays(6);

        return await overtimeService.CalculateOvertimeAsync(request.UserId, weekStart, weekEnd, cancellationToken);
    }
}
