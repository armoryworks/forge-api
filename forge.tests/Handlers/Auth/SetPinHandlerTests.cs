using System.Security.Claims;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class SetPinHandlerTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly SetPinHandler _handler;
    private readonly Faker _faker = new();

    public SetPinHandlerTests()
    {
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new SetPinHandler(_httpContextAccessorMock.Object, _userManagerMock.Object);
    }

    private void SetupHttpContext(int userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessorMock.Setup(x => x.HttpContext)
            .Returns(httpContext);
    }

    [Fact]
    public async Task Handle_ValidPin_StoresHashedPin()
    {
        var user = new ApplicationUser
        {
            Id = 1,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        SetupHttpContext(user.Id);

        _userManagerMock.Setup(x => x.FindByIdAsync("1"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        var command = new SetPinCommand("1234");

        await _handler.Handle(command, CancellationToken.None);

        user.PinHash.Should().NotBeNullOrEmpty();
        user.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);

        // Verify the stored hash can be verified back
        SetPinHandler.VerifyPin("1234", user.PinHash).Should().BeTrue();
        SetPinHandler.VerifyPin("9999", user.PinHash).Should().BeFalse();
    }

    [Fact]
    public void Handle_ShortPin_ThrowsValidationException()
    {
        var validator = new SetPinValidator();

        var result = validator.Validate(new SetPinCommand("12"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Pin");
    }
}
