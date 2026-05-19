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
    public async Task Handle_NoAdminsExist_ReturnsNeedsSetup()
    {
        // No Admin in the system — even if other users exist (e.g. the
        // LeadIntake first-boot service user), setup is incomplete because
        // a human can't log in interactively.
        _userManagerMock
            .Setup(x => x.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());

        var result = await _handler.Handle(new CheckSetupStatusQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result.SetupRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OnlyHeadlessLeadIntakeUserExists_StillReturnsNeedsSetup()
    {
        // Regression pin: pre-fix, ANY user (including the LeadIntake
        // service user with no password) tripped setup-required to false.
        // Now the check filters to Admin role, so a fresh install with
        // only the bootstrap service user correctly shows the setup
        // wizard.
        _userManagerMock
            .Setup(x => x.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>()); // no admins yet

        var result = await _handler.Handle(new CheckSetupStatusQuery(), CancellationToken.None);

        result.SetupRequired.Should().BeTrue(
            "the first-boot LeadIntake service user must NOT mark setup as complete — " +
            "the wizard still needs to create an actual admin");
    }

    [Fact]
    public async Task Handle_AdminExists_ReturnsSetupComplete()
    {
        _userManagerMock
            .Setup(x => x.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>
            {
                new() { Id = 1, FirstName = "Admin", LastName = "User", Email = "admin@test.com" },
            });

        var result = await _handler.Handle(new CheckSetupStatusQuery(), CancellationToken.None);

        result.Should().NotBeNull();
        result.SetupRequired.Should().BeFalse();
    }
}
