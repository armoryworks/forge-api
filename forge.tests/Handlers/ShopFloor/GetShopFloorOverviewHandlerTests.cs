using FluentAssertions;

using Moq;

using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class GetShopFloorOverviewHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClockEventTypeService> _clockEventTypeService = new();
    private readonly Mock<IClock> _clock = new();
    private readonly DateTimeOffset _now = new(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);

    public GetShopFloorOverviewHandlerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _clockEventTypeService.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("clock_in", "Clock In", "In", "clock_out", "work", true, false, "login", "#22c55e"),
            });
    }

    [Fact]
    public async Task Handle_WorkerWithTwoOpenTimers_ShowsTheNewestWithoutThrowing()
    {
        var user = new ApplicationUser
        {
            FirstName = "Ana",
            LastName = "Lopez",
            UserName = "ana@example.com",
            Email = "ana@example.com",
            Initials = "AL",
            IsActive = true,
        };
        _db.Users.Add(user);
        var olderJob = new Job { JobNumber = "J-OLD", Title = "Older job", TrackTypeId = 1, CurrentStageId = 1 };
        var newerJob = new Job { JobNumber = "J-NEW", Title = "Newer job", TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.AddRange(olderJob, newerJob);
        await _db.SaveChangesAsync();

        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = user.Id,
            EventType = ClockEventType.ClockIn,
            EventTypeCode = "clock_in",
            Timestamp = _now.AddHours(-4),
            Source = "kiosk",
        });
        _db.TimeEntries.AddRange(
            new TimeEntry { UserId = user.Id, JobId = olderJob.Id, Date = DateOnly.FromDateTime(_now.UtcDateTime), TimerStart = _now.AddHours(-3) },
            new TimeEntry { UserId = user.Id, JobId = newerJob.Id, Date = DateOnly.FromDateTime(_now.UtcDateTime), TimerStart = _now.AddMinutes(-30) });
        await _db.SaveChangesAsync();

        var handler = new GetShopFloorOverviewHandler(_db, _clockEventTypeService.Object, _clock.Object);

        var result = await handler.Handle(new GetShopFloorOverviewQuery(), CancellationToken.None);

        var worker = result.Workers.Should().ContainSingle().Subject;
        worker.CurrentJobId.Should().Be(newerJob.Id);
        worker.CurrentJobNumber.Should().Be("J-NEW");
        worker.TimeOnTask.Should().Be("30m 00s");
    }
}
