using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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

        _tokenServiceMock = new Mock<ITokenService>();
        _sessionStoreMock = new Mock<ISessionStore>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _auditWriterMock = new Mock<ISystemAuditWriter>();
        _roleClaimsExpanderMock = new Mock<IRoleClaimsExpander>();
        // WU-06 regression: passthrough default returns empty role list. Tests
        // that exercise role expansion override this per-test.
        _roleClaimsExpanderMock
            .Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _db = TestDbContextFactory.Create();
        _handler = new LoginHandler(
            _userManagerMock.Object, _tokenServiceMock.Object,
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
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "ValidPassword1!"))
            .ReturnsAsync(true);
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

        var command = new LoginCommand(user.Email, "ValidPassword1!");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Token.Should().Be("test-jwt-token");
        result.User.Id.Should().Be(user.Id);
        result.User.Email.Should().Be(user.Email);
        result.User.FirstName.Should().Be(user.FirstName);
        result.User.LastName.Should().Be(user.LastName);
        result.User.Roles.Should().Contain("Admin");
        result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));

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

        var command = new LoginCommand("nonexistent@example.com", "password");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_InvalidPassword_ThrowsUnauthorized()
    {
        var user = new ApplicationUser
        {
            Id = 2,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            IsActive = true,
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, It.IsAny<string>()))
            .ReturnsAsync(false);

        var command = new LoginCommand(user.Email, "WrongPassword");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_LockedOutUser_ThrowsUnauthorized()
    {
        var user = new ApplicationUser
        {
            Id = 3,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            IsActive = false,
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);

        var command = new LoginCommand(user.Email, "AnyPassword");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid credentials");
    }
}
