using System.Security.Claims;
using System.Text.Encodings.Web;

using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Authentication;
using Forge.Api.Features.SystemApiKeys;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

// SystemClock lives in both Microsoft.AspNetCore.Authentication (legacy
// abstraction, still referenced by the auth scheme infrastructure) and
// Forge.Platform.Time (our IClock implementation). The handler under test
// takes IClock, so we want the Forge one.
using SystemClock = Forge.Platform.Time.SystemClock;

namespace Forge.Tests.Authentication;

/// <summary>
/// Smoke tests for the SystemApiKey authentication scheme. Exercises the
/// full issue-then-authenticate round-trip end to end:
///
///   1. Seed a service user.
///   2. Issue a key via <see cref="CreateSystemApiKeyHandler"/> (so the
///      handler under test is dogfooding the same hashing path it uses
///      to verify).
///   3. Build a fake HttpContext carrying the plaintext in the standard
///      header.
///   4. Assert: handler returns Success, principal carries the bound
///      user's id as NameIdentifier and roles as Role claims.
///
/// Additional cases: missing header (NoResult), wrong key (Fail),
/// revoked key (Fail), inactive user (Fail).
/// </summary>
public class SystemApiKeyAuthenticationHandlerTests
{
    private const string SchemeName = SystemApiKeyAuthenticationOptions.SchemeName;
    private const string HeaderName = SystemApiKeyAuthenticationOptions.HeaderName;

    private static async Task<(SystemApiKeyAuthenticationHandler handler, HttpContext context, string plaintext, int userId)>
        BuildAsync(string? presentedHeader, bool seedActiveUser = true, bool revokeKey = false)
    {
        var db = TestDbContextFactory.Create();

        // Seed the bound user — mirrors the bootstrap shape.
        var user = new ApplicationUser
        {
            UserName = "svc@example.local",
            Email = "svc@example.local",
            FirstName = "Test", LastName = "Service",
            IsActive = seedActiveUser,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Issue a key via the real handler so the persisted hash matches
        // what HandleAuthenticateAsync will verify.
        var auditWriter = new Mock<ISystemAuditWriter>();
        var issuer = new CreateSystemApiKeyHandler(db, auditWriter.Object);
        var issued = await issuer.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "smoke",
                UserId = user.Id,
            }),
            CancellationToken.None);

        if (revokeKey)
        {
            var keyRow = await db.SystemApiKeys.FindAsync(issued.Id);
            keyRow!.IsActive = false;
            await db.SaveChangesAsync();
        }

        // UserManager mock — GetRolesAsync returns a fixed role list. The
        // handler uses UserManager.GetRolesAsync to hydrate the principal's
        // Role claims; that's the only UserManager surface area we need
        // to mock.
        var userStore = Mock.Of<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore, null!, null!, null!, null!, null!, null!, null!, null!);
        userManagerMock
            .Setup(x => x.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == user.Id)))
            .ReturnsAsync(new List<string> { "LeadIntake" });

        // Authentication scheme + options plumbing.
        var options = new SystemApiKeyAuthenticationOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<SystemApiKeyAuthenticationOptions>>();
        optionsMonitor.Setup(x => x.Get(SchemeName)).Returns(options);
        optionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var clock = new SystemClock();

        var handler = new SystemApiKeyAuthenticationHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            clock,
            auditWriter.Object,
            userManagerMock.Object);

        var context = new DefaultHttpContext();
        if (presentedHeader is not null)
            context.Request.Headers[HeaderName] = presentedHeader;

        var scheme = new AuthenticationScheme(
            SchemeName, displayName: null, handlerType: typeof(SystemApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, context);
        return (handler, context, issued.PlaintextKey, user.Id);
    }

    [Fact]
    public async Task Handle_ValidKey_Succeeds_AndBuildsUserBoundPrincipal()
    {
        // Single-call shape — we need the issued plaintext from setup AND
        // the same handler instance, so we don't use the BuildAsync helper
        // here (which doesn't return the plaintext for the case it later
        // mutates state).
        var db = TestDbContextFactory.Create();
        var user = new ApplicationUser
        {
            UserName = "svc@example.local",
            Email = "svc@example.local",
            FirstName = "Test", LastName = "Service",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var auditWriter = new Mock<ISystemAuditWriter>();
        var issuer = new CreateSystemApiKeyHandler(db, auditWriter.Object);
        var issued = await issuer.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "smoke",
                UserId = user.Id,
            }),
            CancellationToken.None);

        var userStore = Mock.Of<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore, null!, null!, null!, null!, null!, null!, null!, null!);
        userManagerMock
            .Setup(x => x.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == user.Id)))
            .ReturnsAsync(new List<string> { "LeadIntake" });

        var options = new SystemApiKeyAuthenticationOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<SystemApiKeyAuthenticationOptions>>();
        optionsMonitor.Setup(x => x.Get(SchemeName)).Returns(options);
        optionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        var handler = new SystemApiKeyAuthenticationHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            new SystemClock(),
            auditWriter.Object,
            userManagerMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = issued.PlaintextKey;

        var scheme = new AuthenticationScheme(
            SchemeName, displayName: null, handlerType: typeof(SystemApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue(
            "presenting a freshly-issued key in the canonical header authenticates");
        result.Principal.Should().NotBeNull();

        var nameId = result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        nameId.Should().Be(user.Id.ToString(),
            "the principal authenticates AS the bound user — NameIdentifier carries the user id, " +
            "NOT the key id (this is the key contract differentiator from BiApiKey)");

        result.Principal.FindFirstValue(ClaimTypes.Email)
            .Should().Be(user.Email);
        result.Principal.IsInRole("LeadIntake").Should().BeTrue();

        // Auxiliary claims for audit / activity attribution.
        result.Principal.FindFirstValue("system_api_key_prefix")
            .Should().Be(issued.KeyPrefix);
        result.Principal.FindFirstValue("system_api_key_id")
            .Should().Be(issued.Id.ToString());
    }

    [Fact]
    public async Task Handle_NoHeader_ReturnsNoResult()
    {
        var (handler, _, _, _) = await BuildAsync(presentedHeader: null);
        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue(
            "no header at all = let other auth schemes try; NOT a hard 401");
    }

    [Fact]
    public async Task Handle_KeyTooShort_Fails()
    {
        var (handler, _, _, _) = await BuildAsync(presentedHeader: "fsk_short");
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeFalse(
            "presenting a header that's too short to be a real key is a hard fail, not NoResult");
    }

    [Fact]
    public async Task Handle_UnknownKey_Fails()
    {
        var (handler, _, _, _) = await BuildAsync(presentedHeader: "fsk_unknown_xxxxxxxxxxxxxxxxxxxxxxxx");
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RevokedKey_Fails()
    {
        // Issue a key, revoke it, then attempt to authenticate.
        var db = TestDbContextFactory.Create();
        var user = new ApplicationUser
        {
            UserName = "svc@example.local",
            Email = "svc@example.local",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var auditWriter = new Mock<ISystemAuditWriter>();
        var issued = await new CreateSystemApiKeyHandler(db, auditWriter.Object).Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "revoked", UserId = user.Id,
            }),
            CancellationToken.None);

        var keyRow = await db.SystemApiKeys.FindAsync(issued.Id);
        keyRow!.IsActive = false;
        await db.SaveChangesAsync();

        var handler = await BuildHandlerOnAsync(db, user, auditWriter.Object);

        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = issued.PlaintextKey;
        var scheme = new AuthenticationScheme(
            SchemeName, null, typeof(SystemApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);

        var result = await handler.AuthenticateAsync();
        result.Succeeded.Should().BeFalse(
            "a revoked key fails fast at the prefix-lookup phase");
    }

    [Fact]
    public async Task Handle_DeactivatedUser_Fails()
    {
        var db = TestDbContextFactory.Create();
        var user = new ApplicationUser
        {
            UserName = "svc@example.local",
            Email = "svc@example.local",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var auditWriter = new Mock<ISystemAuditWriter>();
        var issued = await new CreateSystemApiKeyHandler(db, auditWriter.Object).Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "user-deactivated-after", UserId = user.Id,
            }),
            CancellationToken.None);

        // Deactivate the user AFTER the key was issued.
        user.IsActive = false;
        await db.SaveChangesAsync();

        var handler = await BuildHandlerOnAsync(db, user, auditWriter.Object);

        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = issued.PlaintextKey;
        var scheme = new AuthenticationScheme(
            SchemeName, null, typeof(SystemApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);

        var result = await handler.AuthenticateAsync();
        result.Succeeded.Should().BeFalse(
            "deactivating the bound user kills all keys bound to it — even if the key row is still IsActive");
    }

    private static Task<SystemApiKeyAuthenticationHandler> BuildHandlerOnAsync(
        AppDbContext db, ApplicationUser user, ISystemAuditWriter auditWriter)
    {
        var userStore = Mock.Of<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore, null!, null!, null!, null!, null!, null!, null!, null!);
        userManagerMock
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string> { "LeadIntake" });

        var options = new SystemApiKeyAuthenticationOptions();
        var optionsMonitor = new Mock<IOptionsMonitor<SystemApiKeyAuthenticationOptions>>();
        optionsMonitor.Setup(x => x.Get(SchemeName)).Returns(options);
        optionsMonitor.Setup(x => x.CurrentValue).Returns(options);

        return Task.FromResult(new SystemApiKeyAuthenticationHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            new SystemClock(),
            auditWriter,
            userManagerMock.Object));
    }
}
