using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Admin;
using Forge.Api.Features.Employees;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class UserDeactivationTests
{
    private readonly TimerTestHarness _h = new();
    private readonly Mock<ISessionStore> _sessions = new();
    private readonly Mock<ISystemAuditWriter> _audit = new();

    public UserDeactivationTests()
    {
        _h.ClockEventTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
                new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
                new("LunchStart", "Start Lunch", "OnLunch", "LunchEnd", "lunch", true, true, "restaurant", "#f97316"),
            });
    }

    private UserDeactivation Deactivation()
        => new(_h.Db, _h.Mediator.Object, _h.ClockEventTypes.Object, _h.Clock.Object, _sessions.Object);

    private DeactivateUserHandler DeactivateHandler()
        => new(_h.Db, Deactivation(), _audit.Object);

    private async Task GrantAdminAsync(params int[] userIds)
    {
        var role = _h.Db.Roles.FirstOrDefault(r => r.Name == UserDeactivation.AdminRole);
        if (role is null)
        {
            role = new IdentityRole<int> { Name = UserDeactivation.AdminRole, NormalizedName = "ADMIN" };
            _h.Db.Roles.Add(role);
            await _h.Db.SaveChangesAsync();
        }
        foreach (var id in userIds)
            _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = id, RoleId = role.Id });
        await _h.Db.SaveChangesAsync();
    }

    private async Task ClockInAsync(int userId, string code, DateTimeOffset at)
    {
        _h.Db.ClockEvents.Add(new ClockEvent { UserId = userId, EventTypeCode = code, Timestamp = at });
        await _h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Deactivate_ClockedInWithRunningTimer_StopsTimerClocksOutAndUnassigns()
    {
        var user = await _h.AddUserAsync();
        var job = await _h.AddJobAsync("J-100");
        job.AssigneeId = user.Id;
        await _h.Db.SaveChangesAsync();
        var timer = await _h.AddRunningTimerAsync(user.Id, job.Id, _h.Now.AddHours(-2));
        await ClockInAsync(user.Id, "ClockIn", _h.Now.AddHours(-3));

        await DeactivateHandler().Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        user.IsActive.Should().BeFalse();
        job.AssigneeId.Should().BeNull();
        timer.TimerStop.Should().Be(_h.Now);
        var latest = _h.Db.ClockEvents.Where(e => e.UserId == user.Id).OrderByDescending(e => e.Timestamp).First();
        latest.EventTypeCode.Should().Be("ClockOut");
        latest.Timestamp.Should().Be(_h.Now);
        latest.Source.Should().Be(UserDeactivation.ClockOutSource);
        latest.Reason.Should().Be(UserDeactivation.ClockOutReason);
        _sessions.Verify(s => s.RevokeAllUserSessionsAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.WriteAsync("UserDeactivated", It.IsAny<int>(), "ApplicationUser", user.Id,
            It.Is<string?>(d => d!.Contains("\"ClockedOut\":true")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_OnLunch_ClocksOut()
    {
        var user = await _h.AddUserAsync();
        await ClockInAsync(user.Id, "LunchStart", _h.Now.AddMinutes(-20));

        await DeactivateHandler().Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        _h.Db.ClockEvents.Count(e => e.UserId == user.Id && e.EventTypeCode == "ClockOut").Should().Be(1);
    }

    [Fact]
    public async Task Deactivate_NotClockedIn_WritesNoClockEvent()
    {
        var user = await _h.AddUserAsync();
        await ClockInAsync(user.Id, "ClockOut", _h.Now.AddHours(-10));

        await DeactivateHandler().Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        _h.Db.ClockEvents.Count(e => e.UserId == user.Id).Should().Be(1);
    }

    [Fact]
    public async Task Deactivate_LastActiveAdmin_ThrowsAndLeavesUserActive()
    {
        var user = await _h.AddUserAsync();
        await GrantAdminAsync(user.Id);

        var act = () => DeactivateHandler().Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(UserDeactivation.LastAdminMessage);
        user.IsActive.Should().BeTrue();
        _sessions.Verify(s => s.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Guard_ServiceAccountAdminDoesNotCountAsAnotherAdmin()
    {
        var user = await _h.AddUserAsync();
        var service = await _h.AddUserAsync();
        await GrantAdminAsync(user.Id, service.Id);
        var leadIntake = new IdentityRole<int> { Name = ServiceAccounts.LeadIntakeRole, NormalizedName = "LEADINTAKE" };
        _h.Db.Roles.Add(leadIntake);
        await _h.Db.SaveChangesAsync();
        _h.Db.UserRoles.Add(new IdentityUserRole<int> { UserId = service.Id, RoleId = leadIntake.Id });
        await _h.Db.SaveChangesAsync();

        var act = () => UserDeactivation.EnsureNotLastActiveAdminAsync(_h.Db, user.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RevokeSystemAccess_LastActiveAdmin_ThrowsAndKeepsLink()
    {
        var user = await _h.AddUserAsync();
        await GrantAdminAsync(user.Id);
        var profile = new EmployeeProfile { UserId = user.Id };
        _h.Db.EmployeeProfiles.Add(profile);
        await _h.Db.SaveChangesAsync();

        var act = () => RevokeHandler().Handle(new RevokeSystemAccessCommand(profile.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        profile.UserId.Should().Be(user.Id);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSystemAccess_ClockedInUser_ClocksOutStopsTimerAndRevokesSessions()
    {
        var user = await _h.AddUserAsync();
        var timer = await _h.AddRunningTimerAsync(user.Id, null, _h.Now.AddHours(-1));
        await ClockInAsync(user.Id, "ClockIn", _h.Now.AddHours(-2));
        var profile = new EmployeeProfile { UserId = user.Id };
        _h.Db.EmployeeProfiles.Add(profile);
        await _h.Db.SaveChangesAsync();

        var ok = await RevokeHandler().Handle(new RevokeSystemAccessCommand(profile.Id), CancellationToken.None);

        ok.Should().BeTrue();
        profile.UserId.Should().BeNull();
        user.IsActive.Should().BeFalse();
        timer.TimerStop.Should().Be(_h.Now);
        _h.Db.ClockEvents.Count(e => e.UserId == user.Id && e.Source == UserDeactivation.ClockOutSource).Should().Be(1);
        _sessions.Verify(s => s.RevokeAllUserSessionsAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private RevokeSystemAccessHandler RevokeHandler()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(u => u.UpdateAsync(It.IsAny<ApplicationUser>()))
            .Returns(async (ApplicationUser u) =>
            {
                await _h.Db.SaveChangesAsync();
                return IdentityResult.Success;
            });
        return new RevokeSystemAccessHandler(
            _h.Db, userManager.Object, _audit.Object, _h.SignedIn(1), Deactivation(), _h.Clock.Object);
    }
}
