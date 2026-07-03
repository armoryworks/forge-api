using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Calendar;

/// <summary>compliance-calendar A-3: delete one of the current user's own saved views.</summary>
public record DeleteCalendarSavedViewCommand(int Id) : IRequest;

public class DeleteCalendarSavedViewHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteCalendarSavedViewCommand>
{
    public async Task Handle(DeleteCalendarSavedViewCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId;

        // Only a user's own personal views are deletable (role-default views have a null owner).
        var view = await db.CalendarSavedViews
            .FirstOrDefaultAsync(v => v.Id == request.Id && v.OwnerUserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"Saved view {request.Id} not found");

        view.DeletedAt = clock.UtcNow; // soft delete; DeletedBy stamped by SetTimestamps
        await db.SaveChangesAsync(cancellationToken);
    }
}
