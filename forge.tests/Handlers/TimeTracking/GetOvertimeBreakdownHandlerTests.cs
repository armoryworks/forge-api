using FluentAssertions;
using Moq;

using Forge.Api.Features.TimeTracking;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TimeTracking;

public class GetOvertimeBreakdownHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOvertimeService> _overtime = new();
    private readonly Mock<IClock> _clock = new();

    public GetOvertimeBreakdownHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _overtime
            .Setup(s => s.CalculateOvertimeAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeBreakdownResponseModel(0, 0, 0, 0, 0, 0, 0, []));
    }

    [Fact]
    public async Task Handle_UnknownUser_IsNotFound()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new GetOvertimeBreakdownHandler(db, _overtime.Object, _clock.Object)
            .Handle(new GetOvertimeBreakdownQuery(999999, new DateOnly(2026, 10, 28)), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*999999*");
        _overtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_NoWeekGiven_UsesTheCurrentShopWeek()
    {
        using var db = TestDbContextFactory.Create();
        db.Users.Add(new ApplicationUser { Id = 7, Email = "ben@forge.local", UserName = "ben@forge.local", FirstName = "Ben", LastName = "Kelly", IsActive = true });
        await db.SaveChangesAsync();

        await new GetOvertimeBreakdownHandler(db, _overtime.Object, _clock.Object)
            .Handle(new GetOvertimeBreakdownQuery(7, null), CancellationToken.None);

        _overtime.Verify(s => s.CalculateOvertimeAsync(7, new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void Validator_RejectsAWeekOutsideTheCalendarRange()
    {
        var validator = new GetOvertimeBreakdownQueryValidator();

        validator.Validate(new GetOvertimeBreakdownQuery(7, DateOnly.MinValue)).IsValid.Should().BeFalse();
        validator.Validate(new GetOvertimeBreakdownQuery(7, null)).IsValid.Should().BeTrue();
        validator.Validate(new GetOvertimeBreakdownQuery(7, new DateOnly(2026, 10, 28))).IsValid.Should().BeTrue();
    }
}
