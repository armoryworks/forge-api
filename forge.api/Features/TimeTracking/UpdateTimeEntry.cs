using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Authorization;
using Forge.Api.Features.ShopFloor;
using Forge.Api.Middleware;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TimeTracking;

public record UpdateTimeEntryCommand(int Id, UpdateTimeEntryRequestModel Data) : IRequest<TimeEntryResponseModel>;

public class UpdateTimeEntryCommandValidator : AbstractValidator<UpdateTimeEntryCommand>
{
    public UpdateTimeEntryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.DurationMinutes).InclusiveBetween(1, 1440).When(x => x.Data.DurationMinutes.HasValue);
        RuleFor(x => x.Data.Category).MaximumLength(100).When(x => x.Data.Category is not null);
        RuleFor(x => x.Data.Notes).MaximumLength(1000).When(x => x.Data.Notes is not null);
    }
}

public class UpdateTimeEntryHandler(
    ITimeTrackingRepository repo, AppDbContext db, IClock clock, IHttpContextAccessor http, IJobCostService? jobCost = null)
    : IRequestHandler<UpdateTimeEntryCommand, TimeEntryResponseModel>
{
    public async Task<TimeEntryResponseModel> Handle(UpdateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindTimeEntryAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry {request.Id} not found.");

        var user = http.HttpContext?.User;
        if (entry.UserId != CallerAccess.UserId(user) && !CallerAccess.IsSupervisor(user))
            throw new ForbiddenException("You can only edit your own time entries.");

        if (entry.IsLocked)
            throw new InvalidOperationException("This time entry is locked and cannot be edited.");

        var shopToday = DateOnly.FromDateTime(ClockStateRules.LocalToday(
            await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken), clock.UtcNow));
        if (entry.Date < shopToday)
            throw new InvalidOperationException("Time entries from previous days cannot be edited.");

        var data = request.Data;
        if (data.Date.HasValue)
            TimeEntryTimes.EnsureNotFuture(data.Date.Value, shopToday);
        if (data.Date.HasValue && data.Date.Value != entry.Date)
            await TimeLockBoundary.EnsureOpenAsync(db, data.Date.Value, cancellationToken);

        if (data.JobId.HasValue) entry.JobId = data.JobId;
        if (data.Date.HasValue) entry.Date = data.Date.Value;
        TimeEntryTimes.Apply(entry, null, null, data.DurationMinutes);
        if (data.Category is not null) entry.Category = data.Category.Trim();
        if (data.Notes is not null) entry.Notes = data.Notes.Trim();
        if (data.IsBillable.HasValue) entry.IsBillable = data.IsBillable.Value;
        if (jobCost is not null && entry.JobId.HasValue)
            await jobCost.PriceTimeEntryAsync(entry, cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);

        return (await repo.GetTimeEntryByIdAsync(entry.Id, cancellationToken))!;
    }
}
