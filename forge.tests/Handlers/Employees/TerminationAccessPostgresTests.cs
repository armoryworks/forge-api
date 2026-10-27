using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Auth;
using Forge.Api.Features.Employees;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Employees;

/// <summary>
/// Termination against real Postgres: the session store's revoke runs ExecuteUpdate, and the
/// kiosk and refresh paths go through real Identity, so the refusal after termination is the
/// one a signed-out worker would actually hit.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerminationAccessPostgresTests(PostgresFixture fixture)
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
    }

    private sealed record Rig(
        AppDbContext Db,
        UserManager<ApplicationUser> Users,
        DbSessionStore Sessions,
        MutableClock Clock,
        EmployeeTermination Termination,
        ITokenService Tokens,
        IRoleClaimsExpander Roles);

    private Rig Build()
    {
        var db = fixture.CreateContext();
        var clock = new MutableClock();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<AppDbContext>();
        var users = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var sessions = new DbSessionStore(db, new MemoryCache(new MemoryCacheOptions()), clock, NullLogger<DbSessionStore>.Instance);
        var types = new Mock<IClockEventTypeService>();
        types.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var mediator = new Mock<IMediator>();
        var deactivation = new UserDeactivation(db, mediator.Object, types.Object, clock, sessions);
        var termination = new EmployeeTermination(db, deactivation, new Mock<ISystemAuditWriter>().Object, mediator.Object, clock);

        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.GenerateToken(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()))
            .Returns(() => new TokenResult("token", Guid.NewGuid().ToString("N"), clock.UtcNow.AddHours(8)));
        var roles = new Mock<IRoleClaimsExpander>();
        roles.Setup(r => r.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ProductionWorker" });

        return new Rig(db, users, sessions, clock, termination, tokens.Object, roles.Object);
    }

    private async Task EnsureReasonAsync(AppDbContext db)
    {
        if (!await db.ReferenceData.AnyAsync(r => r.GroupCode == TerminationReasons.GroupCode && r.Code == "layoff"))
        {
            db.ReferenceData.Add(new ReferenceData { GroupCode = TerminationReasons.GroupCode, Code = "layoff", Label = "Layoff" });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser> AddWorkerAsync(Rig rig, string barcode)
    {
        var user = new ApplicationUser
        {
            UserName = $"{barcode}@termination.test",
            Email = $"{barcode}@termination.test",
            FirstName = "Term",
            LastName = "Probe",
            EmployeeBarcode = barcode,
            PinHash = SetPinHandler.HashPin("4321"),
            IsActive = true,
            EmailConfirmed = true,
        };
        (await rig.Users.CreateAsync(user)).Succeeded.Should().BeTrue();
        rig.Db.EmployeeProfiles.Add(new EmployeeProfile { UserId = user.Id });
        await rig.Db.SaveChangesAsync();
        return user;
    }

    private KioskLoginHandler Kiosk(Rig rig)
        => new(rig.Users, rig.Tokens, rig.Sessions, new Mock<IHttpContextAccessor>().Object, rig.Roles);

    private RefreshTokenHandler Refresh(Rig rig)
    {
        var grace = new Mock<IMfaEnrollmentGrace>();
        grace.Setup(g => g.EvaluateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MfaEnrollmentState.NotRequired);
        return new RefreshTokenHandler(rig.Users, rig.Tokens, rig.Sessions, rig.Roles, grace.Object);
    }

    private static Task<EmployeeTerminationResponseModel> Terminate(Rig rig, int userId, DateOnly date)
        => new TerminateEmployeeHandler(rig.Db, rig.Termination, rig.Clock).Handle(
            new TerminateEmployeeCommand(userId, EmployeeKinds.User,
                new TerminateEmployeeRequestModel(date, "layoff", null, false, false), 0, true),
            CancellationToken.None);

    [Fact]
    public async Task Terminating_now_refuses_the_refresh_token_and_the_kiosk_badge()
    {
        var rig = Build();
        await EnsureReasonAsync(rig.Db);
        var worker = await AddWorkerAsync(rig, $"PG-TERM-{Guid.NewGuid():N}"[..20]);
        var badge = worker.EmployeeBarcode!;

        var kioskLogin = await Kiosk(rig).Handle(new KioskLoginCommand(badge, "4321"), CancellationToken.None);
        var jti = (await rig.Db.UserSessions.AsNoTracking().FirstAsync(s => s.UserId == worker.Id)).Jti;
        (await rig.Sessions.ValidateSessionAsync(jti)).Should().BeTrue();
        kioskLogin.Should().NotBeNull();

        await Terminate(rig, worker.Id, new DateOnly(2026, 10, 7));

        (await rig.Sessions.ValidateSessionAsync(jti)).Should().BeFalse();
        rig.Db.ChangeTracker.Clear();
        await FluentActions.Awaiting(() => Refresh(rig).Handle(new RefreshTokenCommand(jti, worker.Id), CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Awaiting(() => Kiosk(rig).Handle(new KioskLoginCommand(badge, "4321"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid barcode or PIN");

        var saved = await rig.Db.Users.AsNoTracking().SingleAsync(u => u.Id == worker.Id);
        saved.IsActive.Should().BeFalse();
        saved.PinHash.Should().BeNull();
        saved.EmployeeBarcode.Should().BeNull();
    }

    [Fact]
    public async Task A_future_termination_keeps_access_until_the_job_applies_it()
    {
        var rig = Build();
        await EnsureReasonAsync(rig.Db);
        var worker = await AddWorkerAsync(rig, $"PG-FUT-{Guid.NewGuid():N}"[..20]);
        var badge = worker.EmployeeBarcode!;

        await Terminate(rig, worker.Id, new DateOnly(2026, 10, 9));
        await Kiosk(rig).Handle(new KioskLoginCommand(badge, "4321"), CancellationToken.None);

        var job = new ApplyScheduledTerminationsJob(rig.Db, rig.Termination, rig.Clock, NullLogger<ApplyScheduledTerminationsJob>.Instance);
        rig.Clock.UtcNow = new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);
        await job.ApplyDueAsync();

        rig.Db.ChangeTracker.Clear();
        (await rig.Db.EmployeeProfiles.AsNoTracking().SingleAsync(p => p.UserId == worker.Id)).TerminationAppliedAt
            .Should().Be(rig.Clock.UtcNow);
        (await rig.Db.UserSessions.AsNoTracking().Where(s => s.UserId == worker.Id).AllAsync(s => s.RevokedAt != null))
            .Should().BeTrue();
        await FluentActions.Awaiting(() => Kiosk(rig).Handle(new KioskLoginCommand(badge, "4321"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
