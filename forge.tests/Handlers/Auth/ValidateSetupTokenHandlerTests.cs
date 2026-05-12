using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class ValidateSetupTokenHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly ValidateSetupTokenHandler _handler;
    private readonly Faker _faker = new();

    public ValidateSetupTokenHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new ValidateSetupTokenHandler(_userManagerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidToken_ReturnsUserInfo()
    {
        var token = "VALID-TOKEN-123";
        var user = new ApplicationUser
        {
            Id = 1,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            SetupToken = token,
            SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        };

        var users = new List<ApplicationUser> { user }.AsQueryable();
        _userManagerMock.Setup(x => x.Users).Returns(users);

        var query = new ValidateSetupTokenQuery(token);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.FirstName.Should().Be(user.FirstName);
        result.LastName.Should().Be(user.LastName);
        result.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task Handle_InvalidToken_ThrowsKeyNotFoundException()
    {
        var users = new List<ApplicationUser>().AsQueryable();
        _userManagerMock.Setup(x => x.Users).Returns(users);

        var query = new ValidateSetupTokenQuery("NONEXISTENT-TOKEN");

        var act = () => _handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Invalid or expired setup token");
    }

    [Fact]
    public async Task Handle_ExpiredToken_ThrowsKeyNotFoundException()
    {
        var token = "EXPIRED-TOKEN-456";
        var user = new ApplicationUser
        {
            Id = 2,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
            SetupToken = token,
            SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

        var users = new List<ApplicationUser> { user }.AsQueryable();
        _userManagerMock.Setup(x => x.Users).Returns(users);

        var query = new ValidateSetupTokenQuery(token);

        var act = () => _handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Invalid or expired setup token");
    }
}
