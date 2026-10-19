using FluentAssertions;
using FluentValidation;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;

namespace Forge.Tests.Handlers.TimeTracking;

public class TimeEntryTimesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private static TimeEntry Timed() => new()
    {
        TimerStart = Start,
        TimerStop = Start.AddMinutes(60),
        DurationMinutes = 60,
    };

    [Fact]
    public void A_duration_change_moves_the_stop_time()
    {
        var entry = Timed();

        TimeEntryTimes.Apply(entry, null, null, 90);

        entry.DurationMinutes.Should().Be(90);
        entry.TimerStop.Should().Be(Start.AddMinutes(90));
    }

    [Fact]
    public void Moving_one_end_recomputes_the_duration_from_the_stored_other_end()
    {
        var entry = Timed();

        TimeEntryTimes.Apply(entry, Start.AddMinutes(-30), null, null);

        entry.DurationMinutes.Should().Be(90);
        entry.TimerStop.Should().Be(Start.AddMinutes(60));
    }

    [Fact]
    public void A_start_after_the_stored_stop_is_refused()
    {
        var entry = Timed();

        var act = () => TimeEntryTimes.Apply(entry, Start.AddMinutes(120), null, null);

        act.Should().Throw<ValidationException>()
            .Which.Errors.Single().ErrorMessage.Should().Be(TimeEntryTimes.StartAfterEndMessage);
    }

    [Fact]
    public void A_manual_entry_without_timer_times_only_changes_its_duration()
    {
        var entry = new TimeEntry { DurationMinutes = 30, IsManual = true };

        TimeEntryTimes.Apply(entry, null, null, 45);

        entry.DurationMinutes.Should().Be(45);
        entry.TimerStart.Should().BeNull();
        entry.TimerStop.Should().BeNull();
    }
}
