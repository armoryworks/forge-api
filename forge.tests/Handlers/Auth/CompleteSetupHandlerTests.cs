using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using ValidationException = FluentValidation.ValidationException;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class CompleteSetupHandlerTests
{
    private const string Token = "setup-token-value";

    private readonly Mock<IPasswordValidator<ApplicationUser>> _policy = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly ApplicationUser _user;
    private readonly CompleteSetupHandler _handler;

    public CompleteSetupHandlerTests()
    {
        _userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!,
            new[] { _policy.Object }, null!, null!, null!, null!);

        _user = new ApplicationUser
        {
            Id = 7,
            Email = "new.hire@example.test",
            SetupToken = CreateAdminUserHandler.HashSetupToken(Token),
            SetupTokenExpiresAt = DateTimeOffset.MaxValue,
        };
        _userManager.Setup(x => x.Users).Returns(new[] { _user }.AsQueryable());

        _handler = new CompleteSetupHandler(
            _userManager.Object,
            Mock.Of<ITokenService>(),
            Mock.Of<ISessionStore>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IRoleClaimsExpander>(),
            null!);
    }

    [Fact]
    public async Task Handle_PasswordRejectedByPolicy_ReturnsFieldErrorAndLeavesAccountUntouched()
    {
        _policy.Setup(v => v.ValidateAsync(_userManager.Object, _user, "weakpassword"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." }));

        var act = () => _handler.Handle(new CompleteSetupCommand(Token, "weakpassword", null, null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CompleteSetupCommand.Password)
            && e.ErrorMessage.Contains("digit"));
        _userManager.Verify(x => x.RemovePasswordAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _user.SetupToken.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownToken_IsNotReportedAsAPasswordProblem()
    {
        var act = () => _handler.Handle(new CompleteSetupCommand("some-other-token", "Str0ngPassword!", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*setup token*");
    }
}
