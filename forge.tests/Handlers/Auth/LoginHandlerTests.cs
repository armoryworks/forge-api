using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Auth;

public class LoginHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<SignInManager<ApplicationUser>> _signInManagerMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<ISessionStore> _sessionStoreMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ISystemAuditWriter> _auditWriterMock;
    private readonly Mock<IRoleClaimsExpander> _roleClaimsExpanderMock;
    private readonly AppDbContext _db;
    private readonly LoginHandler _handler;
    private readonly Faker _faker = new();

    public LoginHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            _userManagerMock.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, Mock.Of<ILogger<SignInManager<ApplicationUser>>>(), null!, null!);

        _tokenServiceMock = new Mock<ITokenService>();
        _sessionStoreMock = new Mock<ISessionStore>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _auditWriterMock = new Mock<ISystemAuditWriter>();
        _roleClaimsExpanderMock = new Mock<IRoleClaimsExpander>();
        _roleClaimsExpanderMock
            .Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _db = TestDbContextFactory.Create();
        _handler = new LoginHandler(
            _userManagerMock.Object, _signInManagerMock.Object, _tokenServiceMock.Object,
            _sessionStoreMock.Object, _httpContextAccessorMock.Object, _db,
            _auditWriterMock.Object,
            _roleClaimsExpanderMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsTokenAndUser()
    {
        var user = new ApplicationUser
        {
            Id = 1,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            Initials = "JD",
            AvatarColor = "#FF0000",
            IsActive = true,
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, "ValidPassword1!", true))
            .ReturnsAsync(SignInResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });
        _roleClaimsExpanderMock
            .Setup(x => x.GetEffectiveRolesAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Admin" });

        var tokenResult = new TokenResult("test-jwt-token", "test-jti", DateTimeOffset.UtcNow.AddHours(24));
        _tokenServiceMock.Setup(x => x.GenerateToken(
                user.Id, user.Email!, user.FirstName, user.LastName,
                user.Initials, user.AvatarColor,
                It.IsAny<IList<string>>(), null, null))
            .Returns(tokenResult);

        var result = await _handler.Handle(new LoginCommand(user.Email, "ValidPassword1!"), CancellationToken.None);

        result.Should().NotBeNull();
        result.Token.Should().Be("test-jwt-token");
        result.User.Email.Should().Be(user.Email);
        result.User.Roles.Should().Contain("Admin");
        _sessionStoreMock.Verify(x => x.CreateSessionAsync(
            user.Id, "test-jti", It.IsAny<DateTimeOffset>(),
            "credentials", It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidEmail_ThrowsUnauthorized()
    {
        _userManagerMock.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var act = () => _handler.Handle(new LoginCommand("nobody@example.com", "password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_WrongPassword_ThrowsUnauthorized()
    {
        var user = new ApplicationUser
        {
            Id = 2, Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(), LastName = _faker.Name.LastName(), IsActive = true,
        };
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, It.IsAny<string>(), true))
            .ReturnsAsync(SignInResult.Failed);

        var act = () => _handler.Handle(new LoginCommand(user.Email, "WrongPassword"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");
    }

    // ── F-034 lockout regression ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_LockedOutAccount_ThrowsUnauthorized_WithoutCheckingPassword()
    {
        // Arrange: SignInManager reports account locked (already at lockout limit).
        var user = new ApplicationUser
        {
            Id = 3, Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(), LastName = _faker.Name.LastName(), IsActive = true,
        };
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, It.IsAny<string>(), true))
            .ReturnsAsync(SignInResult.LockedOut);

        var act = () => _handler.Handle(new LoginCommand(user.Email, "AnyPassword"), CancellationToken.None);

        // Assert: returns same 401 — no lockout status leak to caller.
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");

        // Token must never be issued for a locked account.
        _tokenServiceMock.Verify(x => x.GenerateToken(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
            It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AccountLocksTriggersBySignInManager_ThrowsUnauthorized()
    {
        // Arrange: this attempt causes the account to lock (SignInResult.LockedOut
        // is returned when the failed attempt crosses the threshold).
        var user = new ApplicationUser
        {
            Id = 4, Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(), LastName = _faker.Name.LastName(), IsActive = true,
        };
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        _signInManagerMock.Setup(x => x.CheckPasswordSignInAsync(user, "WrongPw!", true))
            .ReturnsAsync(SignInResult.LockedOut);

        var act = () => _handler.Handle(new LoginCommand(user.Email, "WrongPw!"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _tokenServiceMock.Verify(x => x.GenerateToken(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
            It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUser_ThrowsUnauthorized_BeforePasswordCheck()
    {
        var user = new ApplicationUser
        {
            Id = 5, Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(), LastName = _faker.Name.LastName(), IsActive = false,
        };
        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);

        var act = () => _handler.Handle(new LoginCommand(user.Email, "AnyPassword"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");

        // Password check must never happen for inactive users.
        _signInManagerMock.Verify(x => x.CheckPasswordSignInAsync(
            It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
