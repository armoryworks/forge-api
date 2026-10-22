using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using ValidationException = FluentValidation.ValidationException;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class CompleteSetupHandlerTests
{
    private const string Token = "SETUP-TOKEN-1";

    private readonly Mock<IPasswordValidator<ApplicationUser>> _policy = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly Mock<ITokenService> _tokens = new();
    private readonly ApplicationUser _user = new()
    {
        Id = 4,
        Email = "new.hire@forge.local",
        FirstName = "New",
        LastName = "Hire",
        SetupToken = Forge.Api.Features.Admin.CreateAdminUserHandler.HashSetupToken(Token),
        SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };

    public CompleteSetupHandlerTests()
    {
        _userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!,
            new[] { _policy.Object }, null!, null!, null!, null!);
        _policy.Setup(v => v.ValidateAsync(It.IsAny<UserManager<ApplicationUser>>(), It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.Users).Returns(new List<ApplicationUser> { _user }.AsQueryable());
        _userManager.Setup(m => m.RemovePasswordAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.AddPasswordAsync(_user, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _tokens.Setup(t => t.GenerateToken(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()))
            .Returns(new TokenResult("jwt", "jti", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private CompleteSetupHandler Handler()
    {
        var roles = new Mock<IRoleClaimsExpander>();
        roles.Setup(r => r.GetEffectiveRolesAsync(It.IsAny<Microsoft.AspNetCore.Identity.IdentityUser<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var grace = new Mock<IMfaEnrollmentGrace>();
        grace.Setup(g => g.EvaluateAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MfaEnrollmentState.NotRequired);
        return new CompleteSetupHandler(
            _userManager.Object, _tokens.Object, Mock.Of<ISessionStore>(), Mock.Of<IHttpContextAccessor>(), roles.Object,
            grace.Object);
    }

    private static IdentityResult ConcurrencyFailure() =>
        IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());

    private Task<LoginResponse> SubmitAsync() =>
        Handler().Handle(new CompleteSetupCommand(Token, "Forge-Setup-2026!", null, null), CancellationToken.None);

    [Fact]
    public async Task Handle_SecondSubmitLosesThePasswordRace_SaysTheLinkWasUsed()
    {
        _userManager.Setup(m => m.AddPasswordAsync(_user, It.IsAny<string>())).ReturnsAsync(ConcurrencyFailure());

        var act = SubmitAsync;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This setup link has already been used.");
    }

    [Fact]
    public async Task Handle_SecondSubmitLosesTheResetRace_SaysTheLinkWasUsed()
    {
        _userManager.Setup(m => m.RemovePasswordAsync(_user)).ReturnsAsync(ConcurrencyFailure());

        var act = SubmitAsync;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This setup link has already been used.");
    }

    [Fact]
    public async Task Handle_TokenClearFails_DoesNotHandOutASession()
    {
        _userManager.Setup(m => m.UpdateAsync(_user)).ReturnsAsync(ConcurrencyFailure());

        var act = SubmitAsync;

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("This setup link has already been used.");
        _tokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WeakPassword_StillReportsTheRequirements()
    {
        _userManager.Setup(m => m.AddPasswordAsync(_user, It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityErrorDescriber().PasswordTooShort(12)));

        var act = SubmitAsync;

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CompleteSetupCommand.Password));
    }

    [Fact]
    public async Task Handle_PasswordRejectedByPolicy_ReturnsFieldErrorAndLeavesAccountUntouched()
    {
        _policy.Setup(v => v.ValidateAsync(_userManager.Object, _user, "weakpassword"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." }));

        var act = () => Handler().Handle(new CompleteSetupCommand(Token, "weakpassword", null, null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CompleteSetupCommand.Password)
            && e.ErrorMessage.Contains("digit"));
        _userManager.Verify(x => x.RemovePasswordAsync(It.IsAny<ApplicationUser>()), Times.Never);
        _user.SetupToken.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownToken_IsNotReportedAsAPasswordProblem()
    {
        var act = () => Handler().Handle(new CompleteSetupCommand("some-other-token", "Str0ngPassword!", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*setup token*");
    }

    [Fact]
    public async Task Handle_ValidToken_ReturnsASession()
    {
        var result = await SubmitAsync();

        result.Token.Should().Be("jwt");
        _user.SetupToken.Should().BeNull();
    }
}
