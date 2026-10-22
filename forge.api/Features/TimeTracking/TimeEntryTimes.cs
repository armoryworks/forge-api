using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Entities;

namespace Forge.Api.Features.TimeTracking;

public static class TimeEntryTimes
{
    public const string StartAfterEndMessage = "Start time must be before end time.";
    public const string FutureDayMessage = "Time can't be logged for a day that hasn't started yet.";
    public const string SpanTooLongMessage = "A time entry can't be longer than 24 hours.";
    public const int MaxMinutes = 1440;

    public static void EnsureNotFuture(DateOnly date, DateOnly shopToday)
    {
        if (date > shopToday)
            throw new ValidationException([new ValidationFailure("Data.Date", FutureDayMessage)]);
    }

    public static (DateTimeOffset Start, DateTimeOffset End)? Span(TimeEntry entry)
        => entry is { IsManual: false, TimerStart: { } start, TimerStop: { } stop } && stop > start ? (start, stop) : null;

    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Spans(
        (DateTimeOffset Start, DateTimeOffset End)? before, TimeEntry after)
        => new[] { before, Span(after) }.Where(s => s.HasValue).Select(s => s!.Value).Distinct().ToList();

    public static void Apply(TimeEntry entry, DateTimeOffset? start, DateTimeOffset? end, int? durationMinutes)
    {
        if (start.HasValue) entry.TimerStart = start;
        if (end.HasValue) entry.TimerStop = end;

        if ((start.HasValue || end.HasValue) && entry.TimerStart is { } from && entry.TimerStop is { } to)
        {
            if (from >= to)
                throw new ValidationException(
                    [new ValidationFailure(start.HasValue ? "Data.StartTime" : "Data.EndTime", StartAfterEndMessage)]);
            var minutes = (int)Math.Round((to - from).TotalMinutes);
            if (minutes > MaxMinutes)
                throw new ValidationException(
                    [new ValidationFailure(end.HasValue ? "Data.EndTime" : "Data.StartTime", SpanTooLongMessage)]);
            entry.DurationMinutes = minutes;
        }
        else if (durationMinutes is int manualMinutes)
        {
            entry.DurationMinutes = manualMinutes;
            if (entry.TimerStart is { } timerStart && entry.TimerStop.HasValue)
                entry.TimerStop = timerStart.AddMinutes(manualMinutes);
        }
    }
}
