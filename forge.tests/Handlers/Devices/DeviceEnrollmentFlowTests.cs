using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Devices;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Devices;

/// <summary>
/// Device enrollment + refresh rotation against real Postgres — the
/// single-winner consume paths and family revocation use
/// ExecuteUpdate, which InMemory cannot run.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeviceEnrollmentFlowTests(PostgresFixture fixture)
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private readonly MutableClock _clock = new();
    private readonly IOptions<MobileOptions> _options = Options.Create(new MobileOptions());

    private Mock<UserManager<ApplicationUser>> MockUserManager(ApplicationUser user)
    {
        var mock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        mock.Setup(x => x.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        return mock;
    }

    private static Mock<IRoleClaimsExpander> MockRoles()
    {
        var mock = new Mock<IRoleClaimsExpander>();
        mock.Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ProductionWorker" });
        return mock;
    }

    private Mock<ITokenService> MockTokens()
    {
        var mock = new Mock<ITokenService>();
        mock.Setup(x => x.GenerateToken(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()))
            .Returns(() => new TokenResult(
                "jwt", Guid.NewGuid().ToString("N"), _clock.UtcNow.AddHours(24)));
        return mock;
    }

    private static Mock<IHttpContextAccessor> MockHttp(int? actorId = null, bool isAdmin = false)
    {
        var ctx = new DefaultHttpContext();
        if (actorId is not null)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actorId.Value.ToString()) };
            if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(x => x.HttpContext).Returns(ctx);
        return mock;
    }

    private async Task<(ApplicationUser user, string rawToken)> SeedUserAndTokenAsync(AppDbContext db)
    {
        var user = new ApplicationUser
        {
            UserName = $"u{Guid.NewGuid():N}@forge.local",
            Email = $"u{Guid.NewGuid():N}@forge.local",
            FirstName = "Test",
            LastName = "Operator",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var raw = OpaqueTokens.NewToken();
        db.DeviceEnrollmentTokens.Add(new DeviceEnrollmentToken
        {
            TokenHash = OpaqueTokens.Sha256Hex(raw),
            TargetUserId = user.Id,
            IsShared = false,
            IssuedByUserId = user.Id,
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();
        return (user, raw);
    }

    private EnrollDeviceHandler EnrollHandler(AppDbContext db, ApplicationUser user) => new(
        db, MockUserManager(user).Object, MockTokens().Object,
        Mock.Of<ISessionStore>(), MockRoles().Object, _clock, _options,
        MockHttp().Object, Mock.Of<ISystemAuditWriter>());

    private RefreshDeviceTokenHandler RefreshHandler(AppDbContext db, ApplicationUser user) => new(
        db, MockUserManager(user).Object, MockTokens().Object,
        Mock.Of<ISessionStore>(), MockRoles().Object, _clock, _options,
        MockHttp().Object, Mock.Of<ISystemAuditWriter>());

    [Fact]
    public async Task Enroll_creates_device_with_refresh_family_and_consumes_token()
    {
        await using var db = fixture.CreateContext();
        var (user, raw) = await SeedUserAndTokenAsync(db);
        var uuid = Guid.NewGuid().ToString();

        var result = await EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, uuid, "Dan's phone", "android", "14", "1.0.0"),
            CancellationToken.None);

        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Id.Should().Be(user.Id);

        var device = await db.UserDevices.SingleAsync(d => d.DeviceUuid == uuid);
        device.UserId.Should().Be(user.Id);
        device.RevokedAt.Should().BeNull();

        (await db.DeviceRefreshTokens.CountAsync(t => t.UserDeviceId == device.Id && t.ConsumedAt == null))
            .Should().Be(1);

        // Second use of the same enrollment token fails.
        var again = () => EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, Guid.NewGuid().ToString(), "Other", "ios", null, null),
            CancellationToken.None);
        await again.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Refresh_rotates_token_and_replaying_the_consumed_one_kills_the_family()
    {
        await using var db = fixture.CreateContext();
        var (user, raw) = await SeedUserAndTokenAsync(db);
        var uuid = Guid.NewGuid().ToString();

        var enrolled = await EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, uuid, "Phone", "android", null, null),
            CancellationToken.None);

        var rotated = await RefreshHandler(db, user).Handle(
            new RefreshDeviceTokenCommand(enrolled.RefreshToken, uuid), CancellationToken.None);
        rotated.RefreshToken.Should().NotBe(enrolled.RefreshToken);

        // Replay of the consumed token: family revoked, device flagged.
        var replay = () => RefreshHandler(db, user).Handle(
            new RefreshDeviceTokenCommand(enrolled.RefreshToken, uuid), CancellationToken.None);
        await replay.Should().ThrowAsync<UnauthorizedAccessException>();

        await using var check = fixture.CreateContext();
        var device = await check.UserDevices.SingleAsync(d => d.DeviceUuid == uuid);
        device.IsFlagged.Should().BeTrue();
        (await check.DeviceRefreshTokens.CountAsync(
            t => t.UserDeviceId == device.Id && t.RevokedAt == null)).Should().Be(0);

        // The rotated successor died with the family too.
        var successor = () => RefreshHandler(db, user).Handle(
            new RefreshDeviceTokenCommand(rotated.RefreshToken, uuid), CancellationToken.None);
        await successor.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Refresh_from_a_revoked_device_returns_the_wipe_signal()
    {
        await using var db = fixture.CreateContext();
        var (user, raw) = await SeedUserAndTokenAsync(db);
        var uuid = Guid.NewGuid().ToString();

        var enrolled = await EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, uuid, "Phone", "ios", null, null),
            CancellationToken.None);

        var device = await db.UserDevices.SingleAsync(d => d.DeviceUuid == uuid);
        await new RevokeDeviceHandler(db, Mock.Of<ISessionStore>(), _clock,
                MockHttp(user.Id, isAdmin: true).Object, Mock.Of<ISystemAuditWriter>())
            .Handle(new RevokeDeviceCommand(device.Id), CancellationToken.None);

        var refresh = () => RefreshHandler(db, user).Handle(
            new RefreshDeviceTokenCommand(enrolled.RefreshToken, uuid), CancellationToken.None);
        await refresh.Should().ThrowAsync<DeviceRevokedException>();
    }

    [Fact]
    public async Task Expired_enrollment_token_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var (user, raw) = await SeedUserAndTokenAsync(db);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(11);

        var act = () => EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, Guid.NewGuid().ToString(), "Phone", "android", null, null),
            CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Non_owner_cannot_revoke_someone_elses_device()
    {
        await using var db = fixture.CreateContext();
        var (user, raw) = await SeedUserAndTokenAsync(db);
        var uuid = Guid.NewGuid().ToString();
        await EnrollHandler(db, user).Handle(
            new EnrollDeviceCommand(raw, uuid, "Phone", "android", null, null),
            CancellationToken.None);
        var device = await db.UserDevices.SingleAsync(d => d.DeviceUuid == uuid);

        var stranger = new RevokeDeviceHandler(db, Mock.Of<ISessionStore>(), _clock,
            MockHttp(user.Id + 12345, isAdmin: false).Object, Mock.Of<ISystemAuditWriter>());

        var act = () => stranger.Handle(new RevokeDeviceCommand(device.Id), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
