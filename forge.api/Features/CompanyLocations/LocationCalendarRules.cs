using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public static class LocationCalendarRules
{
    public static async Task EnsureAssignableAsync(AppDbContext db, int? calendarId, int? currentCalendarId, CancellationToken ct)
    {
        if (calendarId is null || calendarId == currentCalendarId)
            return;

        var calendar = await db.WorkingCalendars
            .AsNoTracking()
            .Where(c => c.Id == calendarId)
            .Select(c => new { c.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Working calendar {calendarId} not found");

        if (!calendar.IsActive)
            throw new InvalidOperationException("An inactive working calendar cannot be assigned to a location.");
    }
}
