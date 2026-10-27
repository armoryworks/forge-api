using FluentAssertions;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class PreviewPayPeriodsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
    private readonly Mock<IClock> _clock = new();

    public PreviewPayPeriodsHandlerTests() => _clock.SetupGet(c => c.UtcNow).Returns(Now);

    [Fact]
    public async Task Biweekly_preview_lists_the_current_period_and_the_next_three()
    {
        using var db = TestDbContextFactory.Create();

        var periods = await new PreviewPayPeriodsHandler(db, _clock.Object)
            .Handle(new PreviewPayPeriodsQuery("biweekly", new DateOnly(2026, 10, 19)), CancellationToken.None);

        periods.Select(p => (p.Start, p.End, p.IsCurrent)).Should().Equal(
            (new DateOnly(2026, 10, 19), new DateOnly(2026, 11, 1), true),
            (new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 15), false),
            (new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 29), false),
            (new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 13), false));
    }

    [Fact]
    public async Task Semimonthly_preview_splits_on_the_fifteenth_and_uses_the_saved_anchor_when_none_is_given()
    {
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting { Key = "pay_period_anchor", Value = "2026-01-05T00:00:00Z" });
        await db.SaveChangesAsync();

        var periods = await new PreviewPayPeriodsHandler(db, _clock.Object)
            .Handle(new PreviewPayPeriodsQuery("semimonthly", null), CancellationToken.None);

        periods.Select(p => (p.Start, p.End)).Should().Equal(
            (new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 31)),
            (new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15)),
            (new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 30)),
            (new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 15)));
    }

    [Fact]
    public void Validator_refuses_an_unknown_period_type()
    {
        new PreviewPayPeriodsValidator().Validate(new PreviewPayPeriodsQuery("fortnightly", null)).IsValid.Should().BeFalse();
        new PreviewPayPeriodsValidator().Validate(new PreviewPayPeriodsQuery("monthly", null)).IsValid.Should().BeTrue();
    }
}
