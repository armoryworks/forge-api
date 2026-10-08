using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using Moq;

using Forge.Api.Features.Mobile;
using Forge.Api.Features.ShopFloor;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.ShopFloor;

public class ClockStateRulesTests
{
    private const string Denver = "America/Denver";

    private static readonly DateTimeOffset FourPmMountainOct7 = new(2026, 10, 7, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SevenPmMountainOct7 = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NoonMountainOct8 = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

    private static readonly List<ClockEventTypeDefinition> EventTypes =
    [
        new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
        new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
        new("BreakStart", "Start Break", "OnBreak", "BreakEnd", "break", true, true, "free_breakfast", "#f59e0b"),
        new("BreakEnd", "End Break", "In", "BreakStart", "break", true, false, "play_arrow", "#22c55e"),
    ];

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClockEventTypeService> _eventTypes = new();
    private readonly Mock<IClock> _clock = new();

    public ClockStateRulesTests()
    {
        _eventTypes.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EventTypes);
    }

    [Fact]
    public async Task ShopDayStartUtcAsync_DefaultCalendarInDenver_ReturnsLocalMidnightAsUtc()
    {
        await AddDefaultCalendarAsync(Denver);

        var dayStart = await ClockStateRules.ShopDayStartUtcAsync(_db, SevenPmMountainOct7);

        dayStart.Should().Be(new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ShopDayStartUtcAsync_NoDefaultCalendar_FallsBackToUtcMidnight()
    {
        var dayStart = await ClockStateRules.ShopDayStartUtcAsync(_db, SevenPmMountainOct7);

        dayStart.Should().Be(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ShopDayStartUtcAsync_UnknownTimeZone_FallsBackToUtcMidnight()
    {
        await AddDefaultCalendarAsync("Not/AZone");

        var dayStart = await ClockStateRules.ShopDayStartUtcAsync(_db, SevenPmMountainOct7);

        dayStart.Should().Be(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ShopDayStartUtcAsync_NoCalendar_UsesDefaultPlantTimeZone()
    {
        _db.Plants.Add(new Plant { Code = "P1", Name = "Plant", TimeZone = Denver, IsDefault = true, IsActive = true });
        await _db.SaveChangesAsync();

        var dayStart = await ClockStateRules.ShopDayStartUtcAsync(_db, SevenPmMountainOct7);

        dayStart.Should().Be(new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ShopDayStartUtcAsync_NoCalendarOrPlant_UsesBookReportingTimeZone()
    {
        _db.Books.Add(new Book { Code = "MAIN", Name = "Default Book", ReportingTimeZone = Denver, IsActive = true });
        await _db.SaveChangesAsync();

        var dayStart = await ClockStateRules.ShopDayStartUtcAsync(_db, SevenPmMountainOct7);

        dayStart.Should().Be(new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task LatestEventsAsync_IgnoresEventsOutsideLookback()
    {
        var user = await AddUserAsync("Old", "Punch");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, SevenPmMountainOct7.AddHours(-169));

        var latest = await ClockStateRules.LatestEventsAsync(_db, [user.Id], SevenPmMountainOct7);

        latest.Should().BeEmpty();
    }

    [Theory]
    [InlineData("In", true, "in")]
    [InlineData("OnBreak", true, "break")]
    [InlineData("OnLunch", true, "break")]
    [InlineData("Out", false, "out")]
    [InlineData("Unknown", false, "out")]
    public void ToPhoneState_MapsStatusMapping(string status, bool countsAsActive, string expected)
    {
        ClockStateRules.ToPhoneState(status, countsAsActive).Should().Be(expected);
    }

    [Fact]
    public void ResolveStatus_LegacyEventWithoutCode_UsesEnumName()
    {
        var evt = new ClockEvent { EventType = ClockEventType.BreakStart, EventTypeCode = "" };

        ClockStateRules.ResolveStatus(evt, EventTypes).Should().Be(("OnBreak", true));
    }

    [Fact]
    public void IsOverdue_ComparesDueDateUtcDateWithShopToday()
    {
        var dueOct7 = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Denver);

        ClockStateRules.IsOverdue(dueOct7, ClockStateRules.LocalToday(tz, SevenPmMountainOct7)).Should().BeFalse();
        ClockStateRules.IsOverdue(dueOct7, ClockStateRules.LocalToday(tz, NoonMountainOct8)).Should().BeTrue();
    }

    [Fact]
    public async Task Kiosk_EveningPunchInDenver_StillInAfterUtcMidnight()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Eve", "Shift");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("In");
        worker.IsClockedIn.Should().BeTrue();
        worker.ClockedInAt.Should().Be(FourPmMountainOct7);
        worker.OpenFromPriorShift.Should().BeFalse();
        worker.OpenSince.Should().BeNull();
    }

    [Fact]
    public async Task Phone_EveningPunchInDenver_StillInAfterUtcMidnight()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Eve", "Phone");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        var handler = new GetClockStateHandler(_db, Mock.Of<IHttpContextAccessor>(), _eventTypes.Object);

        var state = await handler.Handle(new GetClockStateQuery(user.Id), CancellationToken.None);

        state.State.Should().Be("in");
        state.LastEventType.Should().Be("ClockIn");
        state.LastEventAt.Should().Be(FourPmMountainOct7);
    }

    [Fact]
    public async Task Phone_OnBreak_ReturnsBreak()
    {
        var user = await AddUserAsync("Coffee", "Break");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, SevenPmMountainOct7.AddHours(-3));
        await AddEventAsync(user.Id, ClockEventType.BreakStart, SevenPmMountainOct7.AddMinutes(-5));
        var handler = new GetClockStateHandler(_db, Mock.Of<IHttpContextAccessor>(), _eventTypes.Object);

        var state = await handler.Handle(new GetClockStateQuery(user.Id), CancellationToken.None);

        state.State.Should().Be("break");
    }

    [Fact]
    public async Task UserClockStatus_EveningPunchInDenver_StillClockedIn()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Eve", "Desk");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        var handler = new GetUserClockStatusHandler(_db, _eventTypes.Object);

        var status = await handler.Handle(new GetUserClockStatusQuery(user.Id), CancellationToken.None);

        status.IsClockedIn.Should().BeTrue();
        status.Status.Should().Be("In");
        status.ClockedInAt.Should().Be(FourPmMountainOct7);
    }

    [Fact]
    public async Task Kiosk_OpenInFromYesterday_IsFlaggedAndNotReset()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Forgot", "Out");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(NoonMountainOct8);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("In");
        worker.IsClockedIn.Should().BeTrue();
        worker.OpenFromPriorShift.Should().BeTrue();
        worker.OpenSince.Should().Be(FourPmMountainOct7);
    }

    [Fact]
    public async Task Kiosk_NoCalendar_EveningPunchAfterUtcMidnight_IsNotFlagged()
    {
        var user = await AddUserAsync("Late", "Shift");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("In");
        worker.OpenFromPriorShift.Should().BeFalse();
        worker.OpenSince.Should().BeNull();
    }

    [Fact]
    public async Task Kiosk_OvernightShift_IsNotFlaggedMidShift()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Night", "Shift");
        var tenPmMountainOct7 = new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero);
        await AddEventAsync(user.Id, ClockEventType.ClockIn, tenPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(tenPmMountainOct7.AddHours(5));

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("In");
        worker.OpenFromPriorShift.Should().BeFalse();
    }

    [Fact]
    public async Task Kiosk_OpenInOverWeekend_IsFlaggedAndNotReset()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Weekend", "Forgot");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(FourPmMountainOct7.AddHours(63));

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("In");
        worker.OpenFromPriorShift.Should().BeTrue();
        worker.OpenSince.Should().Be(FourPmMountainOct7);
    }

    [Fact]
    public async Task Phone_OpenInFromWeeksAgo_StillIn()
    {
        var user = await AddUserAsync("Weekend", "Phone");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7.AddDays(-30));
        var handler = new GetClockStateHandler(_db, Mock.Of<IHttpContextAccessor>(), _eventTypes.Object);

        var state = await handler.Handle(new GetClockStateQuery(user.Id), CancellationToken.None);

        state.State.Should().Be("in");
        state.LastEventAt.Should().Be(FourPmMountainOct7.AddDays(-30));
    }

    [Fact]
    public async Task UserClockStatus_OpenInFromWeeksAgo_StillClockedIn()
    {
        var user = await AddUserAsync("Weekend", "Desk");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7.AddDays(-30));
        var handler = new GetUserClockStatusHandler(_db, _eventTypes.Object);

        var status = await handler.Handle(new GetUserClockStatusQuery(user.Id), CancellationToken.None);

        status.IsClockedIn.Should().BeTrue();
        status.ClockedInAt.Should().Be(FourPmMountainOct7.AddDays(-30));
    }

    [Fact]
    public async Task Kiosk_ClockedOutYesterday_IsOutAndNotFlagged()
    {
        await AddDefaultCalendarAsync(Denver);
        var user = await AddUserAsync("Went", "Home");
        await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7.AddHours(-8));
        await AddEventAsync(user.Id, ClockEventType.ClockOut, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(NoonMountainOct8);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        var worker = result.Single();
        worker.Status.Should().Be("Out");
        worker.OpenFromPriorShift.Should().BeFalse();
    }

    [Fact]
    public async Task Kiosk_TeamFilter_ListsOnlyActiveEmployeeMembers()
    {
        var onTeam = await AddUserAsync("On", "Team", teamId: 7);
        await AddUserAsync("No", "Team");
        await AddUserAsync("Other", "Team", teamId: 8);
        var contractor = await AddUserAsync("Non", "Employee", teamId: 7);
        contractor.IsNonEmployee = true;
        var former = await AddUserAsync("Left", "Company", teamId: 7);
        former.IsActive = false;
        await _db.SaveChangesAsync();
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(7), CancellationToken.None);

        result.Select(w => w.UserId).Should().Equal(onTeam.Id);
    }

    [Fact]
    public async Task Kiosk_TeamWithNoMembers_ReturnsNoWorkers()
    {
        await AddUserAsync("No", "Team");
        await AddUserAsync("Other", "Team", teamId: 8);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(7), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Overview_TeamFilter_ListsOnlyActiveEmployeeMembers()
    {
        var onTeam = await AddUserAsync("On", "Team", teamId: 7);
        var noTeam = await AddUserAsync("No", "Team");
        var contractor = await AddUserAsync("Non", "Employee", teamId: 7);
        contractor.IsNonEmployee = true;
        var former = await AddUserAsync("Left", "Company", teamId: 7);
        former.IsActive = false;
        await _db.SaveChangesAsync();
        foreach (var user in new[] { onTeam, noTeam, contractor, former })
            await AddEventAsync(user.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await OverviewHandler().Handle(new GetShopFloorOverviewQuery(7), CancellationToken.None);

        result.Workers.Select(w => w.UserId).Should().Equal(onTeam.Id);
    }

    [Fact]
    public async Task Kiosk_DisposedJob_IsExcludedFromAssignments()
    {
        var user = await AddUserAsync("Job", "Holder");
        var stage = await AddShopFloorStageAsync();
        await AddJobAsync("JOB-OPEN", stage, user.Id, disposition: null);
        await AddJobAsync("JOB-SCRAP", stage, user.Id, disposition: JobDisposition.Scrap);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await KioskHandler().Handle(new GetClockStatusQuery(), CancellationToken.None);

        result.Single().Assignments.Select(a => a.JobNumber).Should().Equal("JOB-OPEN");
    }

    [Fact]
    public async Task Overview_DisposedJob_IsExcludedFromActiveJobs()
    {
        var stage = await AddShopFloorStageAsync();
        await AddJobAsync("JOB-OPEN", stage, null, disposition: null);
        await AddJobAsync("JOB-SCRAP", stage, null, disposition: JobDisposition.Scrap);
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await OverviewHandler().Handle(new GetShopFloorOverviewQuery(), CancellationToken.None);

        result.ActiveJobs.Select(j => j.JobNumber).Should().Equal("JOB-OPEN");
    }

    [Fact]
    public async Task Overview_CountsWorkersWhoseMappedStatusIsIn()
    {
        await AddDefaultCalendarAsync(Denver);
        var evening = await AddUserAsync("Eve", "Floor");
        var backFromBreak = await AddUserAsync("Back", "Floor");
        var onBreak = await AddUserAsync("Away", "Floor");
        await AddEventAsync(evening.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        await AddEventAsync(backFromBreak.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        await AddEventAsync(backFromBreak.Id, ClockEventType.BreakEnd, FourPmMountainOct7.AddHours(2));
        await AddEventAsync(onBreak.Id, ClockEventType.ClockIn, FourPmMountainOct7);
        await AddEventAsync(onBreak.Id, ClockEventType.BreakStart, FourPmMountainOct7.AddHours(2));
        _clock.Setup(c => c.UtcNow).Returns(SevenPmMountainOct7);

        var result = await OverviewHandler().Handle(new GetShopFloorOverviewQuery(), CancellationToken.None);

        result.Workers.Select(w => w.UserId).Should().BeEquivalentTo([evening.Id, backFromBreak.Id]);
    }

    private GetClockStatusHandler KioskHandler()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        return new GetClockStatusHandler(_db, userManager.Object, _eventTypes.Object, _clock.Object);
    }

    private GetShopFloorOverviewHandler OverviewHandler()
        => new(_db, _eventTypes.Object, _clock.Object);

    private async Task AddDefaultCalendarAsync(string timeZone)
    {
        _db.WorkingCalendars.Add(new WorkingCalendar { Name = "Plant", TimeZone = timeZone, IsDefault = true });
        await _db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> AddUserAsync(string first, string last, int? teamId = null)
    {
        var email = $"{first}.{last}@example.com".ToLowerInvariant();
        var user = new ApplicationUser
        {
            FirstName = first,
            LastName = last,
            UserName = email,
            Email = email,
            Initials = $"{first[0]}{last[0]}",
            IsActive = true,
            TeamId = teamId,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task AddEventAsync(int userId, ClockEventType type, DateTimeOffset at)
    {
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = userId,
            EventType = type,
            EventTypeCode = type.ToString(),
            Timestamp = at,
            Source = "kiosk",
        });
        await _db.SaveChangesAsync();
    }

    private async Task<JobStage> AddShopFloorStageAsync()
    {
        var track = new TrackType { Name = "Production", Code = "production", IsDefault = true, IsShopFloor = true };
        var stage = new JobStage { Name = "Machining", Code = "machining", SortOrder = 1, IsShopFloor = true, TrackType = track };
        _db.TrackTypes.Add(track);
        _db.JobStages.Add(stage);
        await _db.SaveChangesAsync();
        return stage;
    }

    private async Task AddJobAsync(string jobNumber, JobStage stage, int? assigneeId, JobDisposition? disposition)
    {
        _db.Jobs.Add(new Job
        {
            JobNumber = jobNumber,
            Title = jobNumber,
            TrackTypeId = stage.TrackTypeId,
            CurrentStageId = stage.Id,
            AssigneeId = assigneeId,
            Disposition = disposition,
        });
        await _db.SaveChangesAsync();
    }
}
