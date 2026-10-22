using FluentAssertions;

using Forge.Core.Routing;

namespace Forge.Tests.Routing;

public class PredecessorReleaseTests
{
    private static readonly DateTimeOffset Eight = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static ScheduledSpan Span(int startHour, int endHour, decimal overlap = 0, int? floorHour = null)
        => new(Eight.AddHours(startHour - 8), Eight.AddHours(endHour - 8), floorHour is { } f ? Eight.AddHours(f - 8) : null, overlap);

    [Fact]
    public void No_predecessors_release_nothing()
    {
        PredecessorRelease.From([]).Should().Be(new PredecessorRelease(null, null, null));
    }

    [Fact]
    public void A_join_starts_after_its_last_predecessor_ends()
    {
        var release = PredecessorRelease.From([Span(8, 11), Span(8, 14), Span(9, 12)]);

        release.NotBefore.Should().Be(Eight.AddHours(6));
        release.MustNotEndBefore.Should().BeNull();
        release.Finish.Should().Be(Eight.AddHours(6));
    }

    [Fact]
    public void An_overlapping_predecessor_releases_early_but_holds_the_end()
    {
        var release = PredecessorRelease.From([Span(8, 12, overlap: 25)]);

        release.NotBefore.Should().Be(Eight.AddHours(3));
        release.MustNotEndBefore.Should().Be(Eight.AddHours(4));
    }

    [Fact]
    public void Overlap_over_100_percent_releases_at_the_predecessor_start()
    {
        PredecessorRelease.From([Span(9, 12, overlap: 250)]).NotBefore.Should().Be(Eight.AddHours(1));
    }

    [Fact]
    public void A_pinned_predecessor_whose_floor_is_later_releases_at_the_floor_without_overlap()
    {
        var release = PredecessorRelease.From([Span(8, 10, overlap: 50, floorHour: 13)]);

        release.NotBefore.Should().Be(Eight.AddHours(5));
        release.MustNotEndBefore.Should().BeNull();
        release.Finish.Should().Be(Eight.AddHours(5));
    }

    [Fact]
    public void A_later_plain_predecessor_beats_an_overlapping_one()
    {
        var release = PredecessorRelease.From([Span(8, 12, overlap: 50), Span(8, 13)]);

        release.NotBefore.Should().Be(Eight.AddHours(5));
        release.MustNotEndBefore.Should().Be(Eight.AddHours(4));
    }
}
