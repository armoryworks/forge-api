using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.WorkingCalendars;

public record CreateWorkingCalendarCommand(
    string Name,
    string TimeZone,
    int WorkingDaysMask,
    bool IsActive) : IRequest<WorkingCalendarResponseModel>;

public class CreateWorkingCalendarValidator : AbstractValidator<CreateWorkingCalendarCommand>
{
    public CreateWorkingCalendarValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(64);
        // 7-bit working-days mask. 0 (no working days) is rejected — a
        // calendar with no working days is degenerate.
        RuleFor(x => x.WorkingDaysMask).InclusiveBetween(1, 127);
    }
}

public class CreateWorkingCalendarHandler(AppDbContext db)
    : IRequestHandler<CreateWorkingCalendarCommand, WorkingCalendarResponseModel>
{
    public async Task<WorkingCalendarResponseModel> Handle(CreateWorkingCalendarCommand request, CancellationToken ct)
    {
        var hasAny = await db.WorkingCalendars.AnyAsync(ct);

        var calendar = new WorkingCalendar
        {
            Name = request.Name.Trim(),
            TimeZone = request.TimeZone.Trim(),
            WorkingDaysMask = request.WorkingDaysMask,
            IsActive = request.IsActive,
            // First calendar is always the default — covers the install
            // bootstrap case where there's nothing to fall back to yet.
            IsDefault = !hasAny,
        };

        db.WorkingCalendars.Add(calendar);
        await db.SaveChangesAsync(ct);

        return new WorkingCalendarResponseModel(
            calendar.Id, calendar.Name, calendar.TimeZone, calendar.WorkingDaysMask,
            calendar.IsDefault, calendar.IsActive,
            new List<HolidayResponseModel>(),
            calendar.CreatedAt, calendar.UpdatedAt);
    }
}
