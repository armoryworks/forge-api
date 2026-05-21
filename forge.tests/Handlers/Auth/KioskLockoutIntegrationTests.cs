using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-051 live 6-attempt regression.
///
/// KioskLoginHandlerTests (unit) mock UserManager and prove the handler calls
/// AccessFailedAsync / IsLockedOutAsync correctly.  These tests use a real
/// InMemory EF Identity store to prove the DB layer actually increments
/// access_failed_count, sets lockout_end after the threshold, and blocks the
/// correct-PIN attempt while the account is locked.
///
/// This is the "live 6-attempt regression" required by H-015 DoD.
/// MaxFailedAccessAttempts = 5 (Identity default, not explicitly set in Program.cs).
/// </summary>
public class KioskLockoutIntegrationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMediator _mediator;

    public KioskLockoutIntegrationTests()
    {
        _db = TestDbContextFactory.Create();

        var services = new ServiceCollection();

        // Singleton DB — shared between UserManager's EF store and KioskLoginHandler.
        services.AddSingleton(_db);

        // Real Identity against the InMemory EF store.
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        // Real MediatR — registers KioskLoginHandler.
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblies(typeof(KioskLoginHandler).Assembly));

        // KioskLoginHandler collaborators — mocked; lockout behavior lives in Identity.
        var tokenSvc = new Mock<ITokenService>();
        tokenSvc
            .Setup(x => x.GenerateToken(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()))
            .Returns(new TokenResult("live-test-token", "live-jti", DateTimeOffset.UtcNow.AddHours(8)));
        services.AddSingleton(tokenSvc.Object);

        var sessionStore = new Mock<ISessionStore>();
        services.AddSingleton(sessionStore.Object);

        var roleExpander = new Mock<IRoleClaimsExpander>();
        roleExpander
            .Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ProductionWorker" });
        services.AddSingleton(roleExpander.Object);

        var httpCtx = new Mock<IHttpContextAccessor>();
        httpCtx.Setup(x => x.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1")]))
        });
        services.AddSingleton(httpCtx.Object);

        services.AddLogging();

        var root = services.BuildServiceProvider();
        _scope = root.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    private async Task<ApplicationUser> CreateKioskUserAsync(string barcode, string pin)
    {
        var user = new ApplicationUser
        {
            UserName = $"{barcode}@kiosk.lockout.test",
            Email = $"{barcode}@kiosk.lockout.test",
            FirstName = "Lockout",
            LastName = "Probe",
            EmployeeBarcode = barcode,
            PinHash = SetPinHandler.HashPin(pin),
            IsActive = true,
            EmailConfirmed = true,
        };
        var result = await _userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue("probe user creation must succeed");
        return user;
    }

    // ── F-051 live regression 1: 5 wrong PINs → lockout fires; 6th (correct) PIN blocked ────

    [Fact]
    public async Task FiveWrongPins_LocksAccount_SixthCorrectPinStillRejected()
    {
        // Arrange
        const string correctPin = "7777";
        const string wrongPin = "0000";
        await CreateKioskUserAsync("LOCK-PROBE-A", correctPin);

        // Act — fire MaxFailedAccessAttempts (5) wrong PINs through real Identity store.
        // Each attempt: handler → AccessFailedAsync → Identity increments access_failed_count.
        // After the 5th wrong PIN Identity sets lockout_end and resets the count.
        for (var i = 1; i <= 5; i++)
        {
            var attempt = () => _mediator.Send(new KioskLoginCommand("LOCK-PROBE-A", wrongPin), CancellationToken.None);
            await attempt.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Invalid barcode or PIN",
                    $"wrong-PIN attempt {i}/5 must be rejected");
        }

        // Assert 1: account is now locked at the Identity DB layer.
        _db.ChangeTracker.Clear();
        var lockedUser = await _userManager.FindByEmailAsync("LOCK-PROBE-A@kiosk.lockout.test");
        (await _userManager.IsLockedOutAsync(lockedUser!)).Should().BeTrue(
            "Identity must set LockoutEnd after MaxFailedAccessAttempts=5 wrong PINs (F-051)");

        // Assert 2: correct PIN is still rejected while the lock is active —
        // handler checks IsLockedOutAsync first (KioskLogin.cs:39) and never reaches VerifyPin.
        var sixthAttempt = () => _mediator.Send(new KioskLoginCommand("LOCK-PROBE-A", correctPin), CancellationToken.None);
        await sixthAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid barcode or PIN",
                "correct PIN must be rejected while account is locked (lock must persist until expiry — F-051)");
    }

    // ── F-051 live regression 2: successful login resets access-failed count ──────────────

    [Fact]
    public async Task PartialFailures_ThenSuccessfulLogin_ResetsAccessFailedCount()
    {
        // Arrange — fire 2 wrong PINs (below lockout threshold), then correct PIN.
        const string correctPin = "4321";
        const string wrongPin = "9999";
        await CreateKioskUserAsync("LOCK-PROBE-B", correctPin);

        for (var i = 0; i < 2; i++)
        {
            var attempt = () => _mediator.Send(new KioskLoginCommand("LOCK-PROBE-B", wrongPin), CancellationToken.None);
            await attempt.Should().ThrowAsync<InvalidOperationException>();
        }

        // Act — correct PIN succeeds; handler must call ResetAccessFailedCountAsync.
        var result = await _mediator.Send(new KioskLoginCommand("LOCK-PROBE-B", correctPin), CancellationToken.None);

        // Assert: login response is valid and counter was reset at the DB layer.
        result.Should().NotBeNull();
        result.Token.Should().Be("live-test-token");

        _db.ChangeTracker.Clear();
        var freshUser = await _userManager.FindByEmailAsync("LOCK-PROBE-B@kiosk.lockout.test");
        (await _userManager.GetAccessFailedCountAsync(freshUser!)).Should().Be(0,
            "KioskLoginHandler must call ResetAccessFailedCountAsync on successful login (F-051)");
        (await _userManager.IsLockedOutAsync(freshUser!)).Should().BeFalse();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _db.Dispose();
    }
}
