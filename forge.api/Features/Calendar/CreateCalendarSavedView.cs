using FluentValidation;
using MediatR;

using Forge.Core.Entities.Calendar;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Calendar;

/// <summary>compliance-calendar A-3: save the current overlay-layer selection as a personal view.</summary>
public record CreateCalendarSavedViewCommand(
    string Name, string Scope, int[] SelectedSuperGroupIds, int[] SelectedEventTypeIds)
    : IRequest<CalendarSavedViewResponseModel>;

public class CreateCalendarSavedViewValidator : AbstractValidator<CreateCalendarSavedViewCommand>
{
    public CreateCalendarSavedViewValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Scope).NotEmpty().MaximumLength(60);
    }
}

public class CreateCalendarSavedViewHandler(AppDbContext db)
    : IRequestHandler<CreateCalendarSavedViewCommand, CalendarSavedViewResponseModel>
{
    public async Task<CalendarSavedViewResponseModel> Handle(
        CreateCalendarSavedViewCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("A saved view requires an authenticated user.");

        var view = new CalendarSavedView
        {
            Name = request.Name,
            OwnerUserId = userId,
            Scope = request.Scope,
            SelectedSuperGroupIds = request.SelectedSuperGroupIds,
            SelectedEventTypeIds = request.SelectedEventTypeIds,
            IsDefault = false,
        };
        db.CalendarSavedViews.Add(view);
        await db.SaveChangesAsync(cancellationToken);

        return new CalendarSavedViewResponseModel(
            view.Id, view.Name, view.OwnerUserId, view.RoleKey, view.Scope,
            view.SelectedSuperGroupIds, view.SelectedEventTypeIds, view.IsDefault);
    }
}
