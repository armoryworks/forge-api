using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Entities;

namespace Forge.Api.Features.TimeTracking;

public static class TimeEntryTimes
{
    public const string StartAfterEndMessage = "Start time must be before end time.";

    public static void Apply(TimeEntry entry, DateTimeOffset? start, DateTimeOffset? end, int? durationMinutes)
    {
        if (start.HasValue) entry.TimerStart = start;
        if (end.HasValue) entry.TimerStop = end;

        if ((start.HasValue || end.HasValue) && entry.TimerStart is { } from && entry.TimerStop is { } to)
        {
            if (from >= to)
                throw new ValidationException(
                    [new ValidationFailure(start.HasValue ? "Data.StartTime" : "Data.EndTime", StartAfterEndMessage)]);
            entry.DurationMinutes = (int)Math.Round((to - from).TotalMinutes);
        }
        else if (durationMinutes is int minutes)
        {
            entry.DurationMinutes = minutes;
            if (entry.TimerStart is { } timerStart && entry.TimerStop.HasValue)
                entry.TimerStop = timerStart.AddMinutes(minutes);
        }
    }
}
