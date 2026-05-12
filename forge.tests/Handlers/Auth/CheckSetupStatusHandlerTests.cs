using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class CheckSetupStatusHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly CheckSetupStatusHandler _handler;

    public CheckSetupStatusHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _handler = new CheckSetupStatusHandler(_userManagerMock.Object);
    }

    [Fact]
    public async Task Handle_NoUsersExist_ReturnsNeedsSetup()
    {
        var users = new List<ApplicationUser>().AsQueryable();
        _userManagerMock.Setup(x => x.Users)
            .Returns(users);

        var result = await _handler.Handle(new CheckSetupStatusQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result.SetupRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UsersExist_ReturnsSetupComplete()
    {
        var users = new List<ApplicationUser>
        {
            new() { Id = 1, FirstName = "Admin", LastName = "User", Email = "admin@test.com" },
        }.AsQueryable();

        _userManagerMock.Setup(x => x.Users)
            .Returns(users);

        var result = await _handler.Handle(new CheckSetupStatusQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result.SetupRequired.Should().BeFalse();
    }
}
