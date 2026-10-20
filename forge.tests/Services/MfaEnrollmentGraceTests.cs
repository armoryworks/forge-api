using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class MfaEnrollmentGraceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly SettableClock _clock = new() { UtcNow = Start };

    private sealed class SettableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private MfaEnrollmentGrace Grace() => new(
        _db, new SystemAuditWriter(_db, Mock.Of<IHttpContextAccessor>(), _clock), _clock);

    private async Task<ApplicationUser> SeedFlaggedUserAsync(int? graceDays = null)
    {
        var user = new ApplicationUser
        {
            UserName = "controller@forge.test",
            Email = "controller@forge.test",
            FirstName = "Grace",
            LastName = "Probe",
            IsActive = true,
        };
        _db.Users.Add(user);
        var role = new IdentityRole<int>("Controller") { NormalizedName = "CONTROLLER" };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await MfaRolePolicy.SetRequiredRolesAsync(_db, ["Controller"], CancellationToken.None);
        if (graceDays is { } days)
            await MfaRolePolicy.SetGraceDaysAsync(_db, days, CancellationToken.None);
        await _db.SaveChangesAsync();
        return user;
    }

    private Task<int> AuditCountAsync(string action)
        => _db.AuditLogEntries.CountAsync(e => e.Action == action);

    [Fact]
    public async Task FirstSignInWhileFlagged_StartsTheWindow_AndAuditsIt()
    {
        var user = await SeedFlaggedUserAsync();

        var state = await Grace().EvaluateAsync(user);

        state.Hold.Should().BeFalse();
        state.GraceEndsAt.Should().Be(Start.AddDays(MfaRolePolicy.DefaultGraceDays));
        (await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).MfaEnrollmentGraceStartedAt.Should().Be(Start);
        (await AuditCountAsync(MfaEnrollmentGrace.StartedAction)).Should().Be(1);
    }

    [Fact]
    public async Task WithinTheWindow_KeepsAccess_WithoutRestartingIt()
    {
        var user = await SeedFlaggedUserAsync(graceDays: 7);
        await Grace().EvaluateAsync(user);

        _clock.UtcNow = Start.AddDays(6).AddHours(23);
        var state = await Grace().EvaluateAsync(user);

        state.Hold.Should().BeFalse();
        state.GraceEndsAt.Should().Be(Start.AddDays(7));
        user.MfaEnrollmentGraceStartedAt.Should().Be(Start);
        (await AuditCountAsync(MfaEnrollmentGrace.StartedAction)).Should().Be(1);
        (await AuditCountAsync(MfaEnrollmentGrace.LapsedAction)).Should().Be(0);
    }

    [Fact]
    public async Task AfterTheWindow_HoldsTheUser_AndAuditsTheLapseOnce()
    {
        var user = await SeedFlaggedUserAsync(graceDays: 7);
        await Grace().EvaluateAsync(user);

        _clock.UtcNow = Start.AddDays(7);
        var lapsed = await Grace().EvaluateAsync(user);
        _clock.UtcNow = Start.AddDays(8);
        var stillHeld = await Grace().EvaluateAsync(user);

        lapsed.Should().Be(new MfaEnrollmentState(true, null));
        stillHeld.Should().Be(new MfaEnrollmentState(true, null));
        (await AuditCountAsync(MfaEnrollmentGrace.LapsedAction)).Should().Be(1);
    }

    [Fact]
    public async Task ZeroGraceDays_HoldsImmediately_WithoutOpeningAWindow()
    {
        var user = await SeedFlaggedUserAsync(graceDays: 0);

        var state = await Grace().EvaluateAsync(user);

        state.Should().Be(new MfaEnrollmentState(true, null));
        user.MfaEnrollmentGraceStartedAt.Should().BeNull();
        (await AuditCountAsync(MfaEnrollmentGrace.StartedAction)).Should().Be(0);
    }

    [Fact]
    public async Task LoweringGraceToZero_LapsesAnOpenWindow()
    {
        var user = await SeedFlaggedUserAsync(graceDays: 7);
        await Grace().EvaluateAsync(user);
        await MfaRolePolicy.SetGraceDaysAsync(_db, 0, CancellationToken.None);
        await _db.SaveChangesAsync();

        _clock.UtcNow = Start.AddHours(1);
        var state = await Grace().EvaluateAsync(user);

        state.Hold.Should().BeTrue();
        (await AuditCountAsync(MfaEnrollmentGrace.LapsedAction)).Should().Be(1);
    }

    [Fact]
    public async Task Enrolling_ClearsTheWindow()
    {
        var user = await SeedFlaggedUserAsync();
        await Grace().EvaluateAsync(user);

        user.MfaEnabled = true;
        await _db.SaveChangesAsync();
        var state = await Grace().EvaluateAsync(user);

        state.Should().Be(MfaEnrollmentState.NotRequired);
        (await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).MfaEnrollmentGraceStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task ClearingTheFlag_ClearsTheWindow_AndAReflagStartsAFreshOne()
    {
        var user = await SeedFlaggedUserAsync();
        await Grace().EvaluateAsync(user);

        await new SetMfaPolicyHandler(_db, MockRoles().Object)
            .Handle(new SetMfaPolicyCommand([]), CancellationToken.None);
        user.MfaEnrollmentGraceStartedAt.Should().BeNull();
        (await Grace().EvaluateAsync(user)).Should().Be(MfaEnrollmentState.NotRequired);

        await new SetMfaPolicyHandler(_db, MockRoles().Object)
            .Handle(new SetMfaPolicyCommand(["Controller"]), CancellationToken.None);
        _clock.UtcNow = Start.AddDays(30);
        var state = await Grace().EvaluateAsync(user);

        state.Should().Be(new MfaEnrollmentState(false, Start.AddDays(30 + MfaRolePolicy.DefaultGraceDays)));
        (await AuditCountAsync(MfaEnrollmentGrace.StartedAction)).Should().Be(2);
    }

    [Fact]
    public async Task UserOutsideThePolicy_HasNoWindow()
    {
        var user = await SeedFlaggedUserAsync();
        await MfaRolePolicy.SetRequiredRolesAsync(_db, ["Admin"], CancellationToken.None);
        await _db.SaveChangesAsync();

        (await Grace().EvaluateAsync(user)).Should().Be(MfaEnrollmentState.NotRequired);
        user.MfaEnrollmentGraceStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task SavingThePolicy_StoresTheGraceDays()
    {
        await new SetMfaPolicyHandler(_db, MockRoles().Object)
            .Handle(new SetMfaPolicyCommand(["Controller"], 14), CancellationToken.None);

        var policy = await new GetMfaPolicyHandler(_db).Handle(new GetMfaPolicyQuery(), CancellationToken.None);

        policy.EnrollmentGraceDays.Should().Be(14);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void GraceDays_AreBounded(int days, bool valid)
        => new SetMfaPolicyValidator().Validate(new SetMfaPolicyCommand([], days)).IsValid.Should().Be(valid);

    private Mock<UserManager<ApplicationUser>> MockRoles()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .Returns((ApplicationUser u) => Task.FromResult<IList<string>>(_db.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToList()));
        return userManager;
    }

    public void Dispose() => _db.Dispose();
}
