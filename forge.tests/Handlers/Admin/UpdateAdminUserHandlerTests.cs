using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Admin;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class UpdateAdminUserHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ISystemAuditWriter> _auditWriterMock;
    private readonly Mock<ISessionStore> _sessionStoreMock = new();
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly AppDbContext _db;
    private readonly UpdateAdminUserHandler _handler;

    public UpdateAdminUserHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _auditWriterMock = new Mock<ISystemAuditWriter>();
        _db = TestDbContextFactory.Create();

        _userManagerMock.Setup(x => x.NormalizeEmail(It.IsAny<string>()))
            .Returns((string? e) => e?.ToUpperInvariant() ?? string.Empty);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());
        _userManagerMock.Setup(x => x.HasPasswordAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(true);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 7, 14, 0, 0, TimeSpan.Zero));
        var clockEventTypes = new Mock<IClockEventTypeService>();
        clockEventTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockEventTypeDefinition>
            {
                new("ClockIn", "Clock In", "In", "ClockOut", "work", true, true, "login", "#22c55e"),
                new("ClockOut", "Clock Out", "Out", "ClockIn", "work", false, false, "logout", "#ef4444"),
            });
        var deactivation = new UserDeactivation(
            _db, _mediatorMock.Object, clockEventTypes.Object, clock.Object, _sessionStoreMock.Object);

        _handler = new UpdateAdminUserHandler(
            _userManagerMock.Object, _db, _auditWriterMock.Object, new StubCapabilitySnapshotProvider("CAP-HR-HIRE"),
            deactivation, clock.Object);
    }

    private void GrantAdmin(params int[] userIds)
    {
        if (!_db.Roles.Any(r => r.Name == UserDeactivation.AdminRole))
            _db.Roles.Add(new IdentityRole<int> { Id = 1, Name = UserDeactivation.AdminRole, NormalizedName = "ADMIN" });
        foreach (var id in userIds)
            _db.UserRoles.Add(new IdentityUserRole<int> { UserId = id, RoleId = 1 });
        _db.SaveChanges();
    }

    private ApplicationUser SeedUser(int id, string email)
    {
        var user = new ApplicationUser
        {
            Id = id,
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            FirstName = "First",
            LastName = "Last",
            Initials = "FL",
            AvatarColor = "#0d9488",
            IsActive = true,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Handle_EmailChanged_SyncsEmailAndUserNameAndWritesAudit()
    {
        var user = SeedUser(1, "typo@example.com");

        var result = await _handler.Handle(
            new UpdateAdminUserCommand(1, null, null, null, null, null, null, Email: "correct@example.com"),
            CancellationToken.None);

        result.Email.Should().Be("correct@example.com");
        user.Email.Should().Be("correct@example.com");
        // Email is the login identity — UserName must follow it.
        user.UserName.Should().Be("correct@example.com");
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        _auditWriterMock.Verify(x => x.WriteAsync(
            "UserEmailChanged", It.IsAny<int>(), "ApplicationUser", 1,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailUnchanged_DoesNotWriteEmailAudit()
    {
        SeedUser(1, "same@example.com");

        await _handler.Handle(
            new UpdateAdminUserCommand(1, "NewFirst", null, null, null, null, null, Email: "same@example.com"),
            CancellationToken.None);

        _auditWriterMock.Verify(x => x.WriteAsync(
            "UserEmailChanged", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailAlreadyInUse_ThrowsInvalidOperationException()
    {
        SeedUser(1, "first@example.com");
        SeedUser(2, "taken@example.com");

        var act = () => _handler.Handle(
            new UpdateAdminUserCommand(1, null, null, null, null, null, null, Email: "taken@example.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in use*");
        _userManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Deactivating_RevokesAllSessionsOfThatUser()
    {
        var user = SeedUser(7, "pm@example.com");

        await _handler.Handle(
            new UpdateAdminUserCommand(7, null, null, null, null, false, null),
            CancellationToken.None);

        user.IsActive.Should().BeFalse();
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyInactiveOrStayingActive_DoesNotRevokeSessions()
    {
        var inactive = SeedUser(8, "gone@example.com");
        inactive.IsActive = false;
        _db.SaveChanges();
        SeedUser(9, "here@example.com");

        await _handler.Handle(
            new UpdateAdminUserCommand(8, null, null, null, null, false, null),
            CancellationToken.None);
        await _handler.Handle(
            new UpdateAdminUserCommand(9, "Renamed", null, null, null, true, null),
            CancellationToken.None);

        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ServiceAccount_IsFlagged_AndNeverAssignable()
    {
        var user = SeedUser(10, "lead-intake-system@forge.local");
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "LeadIntake" });
        _userManagerMock.Setup(x => x.HasPasswordAsync(user)).ReturnsAsync(false);

        var result = await _handler.Handle(
            new UpdateAdminUserCommand(10, null, null, null, null, null, null),
            CancellationToken.None);

        result.IsServiceAccount.Should().BeTrue();
        result.CanBeAssignedJobs.Should().BeFalse();
        result.MissingComplianceItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DeactivatingLastActiveAdmin_ThrowsBeforeSaving()
    {
        var user = SeedUser(20, "only-admin@example.com");
        GrantAdmin(20);

        var act = () => _handler.Handle(
            new UpdateAdminUserCommand(20, null, null, null, null, false, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active Admin*");
        user.IsActive.Should().BeTrue();
        _userManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RemovingAdminRoleFromLastActiveAdmin_Throws()
    {
        SeedUser(21, "only-admin@example.com");
        GrantAdmin(21);

        var act = () => _handler.Handle(
            new UpdateAdminUserCommand(21, null, null, null, null, null, ["Manager"]),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active Admin*");
        _userManagerMock.Verify(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveSecondAdmin_DoesNotCount()
    {
        SeedUser(22, "admin@example.com");
        var other = SeedUser(23, "former-admin@example.com");
        other.IsActive = false;
        _db.SaveChanges();
        GrantAdmin(22, 23);

        var act = () => _handler.Handle(
            new UpdateAdminUserCommand(22, null, null, null, null, false, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_DeactivatingAdminWhileAnotherActiveAdminExists_Succeeds()
    {
        var user = SeedUser(24, "admin-a@example.com");
        SeedUser(25, "admin-b@example.com");
        GrantAdmin(24, 25);

        await _handler.Handle(
            new UpdateAdminUserCommand(24, null, null, null, null, false, null),
            CancellationToken.None);

        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_KeepingAdminRole_DoesNotTripTheGuard()
    {
        SeedUser(26, "only-admin@example.com");
        GrantAdmin(26);

        await _handler.Handle(
            new UpdateAdminUserCommand(26, "Renamed", null, null, null, true, ["Admin", "Manager"]),
            CancellationToken.None);
    }

    [Fact]
    public async Task Handle_Deactivating_UnassignsOpenJobsStopsTimersAndClocksOut()
    {
        SeedUser(27, "machinist@example.com");
        _db.Jobs.AddRange(
            new Job { Id = 501, JobNumber = "J-501", Title = "Open", TrackTypeId = 1, CurrentStageId = 1, AssigneeId = 27 },
            new Job { Id = 502, JobNumber = "J-502", Title = "Done", TrackTypeId = 1, CurrentStageId = 1, AssigneeId = 27,
                CompletedDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) },
            new Job { Id = 503, JobNumber = "J-503", Title = "Archived", TrackTypeId = 1, CurrentStageId = 1, AssigneeId = 27, IsArchived = true });
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = 27, EventTypeCode = "ClockIn", Timestamp = new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero),
        });
        _db.SaveChanges();

        await _handler.Handle(
            new UpdateAdminUserCommand(27, null, null, null, null, false, null),
            CancellationToken.None);

        _db.Jobs.Single(j => j.Id == 501).AssigneeId.Should().BeNull();
        _db.Jobs.Single(j => j.Id == 502).AssigneeId.Should().Be(27);
        _db.Jobs.Single(j => j.Id == 503).AssigneeId.Should().Be(27);
        _mediatorMock.Verify(m => m.Send(
            It.Is<StopActiveTimerCommand>(c => c.UserId == 27 && c.IncludeOperationTimers),
            It.IsAny<CancellationToken>()), Times.Once);
        var clockOut = _db.ClockEvents.Where(e => e.UserId == 27).OrderByDescending(e => e.Timestamp).First();
        clockOut.EventTypeCode.Should().Be("ClockOut");
        clockOut.Source.Should().Be(UserDeactivation.ClockOutSource);
        clockOut.Reason.Should().Be(UserDeactivation.ClockOutReason);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(27, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeactivatingUserWhoIsClockedOut_WritesNoClockEvent()
    {
        SeedUser(28, "office@example.com");
        _db.ClockEvents.Add(new ClockEvent
        {
            UserId = 28, EventTypeCode = "ClockOut", Timestamp = new DateTimeOffset(2026, 10, 6, 22, 0, 0, TimeSpan.Zero),
        });
        _db.SaveChanges();

        await _handler.Handle(
            new UpdateAdminUserCommand(28, null, null, null, null, false, null),
            CancellationToken.None);

        _db.ClockEvents.Count(e => e.UserId == 28).Should().Be(1);
    }
}
