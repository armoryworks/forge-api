using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Auth;
using Forge.Api.Services;

namespace Forge.Tests.Handlers.Auth;

public class SsoCallbackEmailVerificationTests
{
    [Theory]
    [InlineData("google", "true", true)]
    [InlineData("google", "True", true)]
    [InlineData("google", "false", false)]
    [InlineData("google", null, false)]
    [InlineData("microsoft", null, true)]
    [InlineData("microsoft", "false", false)]
    [InlineData("oidc", null, true)]
    [InlineData("oidc", "true", true)]
    [InlineData("oidc", "false", false)]
    public void IsTrusted_FollowsTheTokenExchangeRule(string provider, string? emailVerified, bool expected)
        => SsoEmailVerification.IsTrusted(provider, emailVerified).Should().Be(expected);

    [Fact]
    public async Task BrowserCallback_WithUnverifiedEmail_IsRefused_BeforeAnyAccountLookup()
    {
        var mediator = new Mock<IMediator>();
        var (controller, auth) = Controller(mediator, "oidc-sub-1", "victim@forge.test", emailVerified: "false");

        var result = await controller.SsoCallback("oidc");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/sso/callback?error=email_unverified");
        mediator.Verify(m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        auth.Verify(a => a.SignOutAsync(It.IsAny<HttpContext>(), "SsoExternalCookie", It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    [Fact]
    public async Task BrowserCallback_WithVerifiedEmail_ProceedsToTheAccountLookup()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No account found."));
        var (controller, _) = Controller(mediator, "oidc-sub-2", "someone@forge.test", emailVerified: "true");

        var result = await controller.SsoCallback("oidc");

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/sso/callback?error=no_account");
        mediator.Verify(m => m.Send(
            It.Is<SsoCallbackCommand>(c => c.Email == "someone@forge.test" && c.ExternalId == "oidc-sub-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (AuthController, Mock<IAuthenticationService>) Controller(
        Mock<IMediator> mediator, string subject, string email, string? emailVerified)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Email, email),
        };
        if (emailVerified is not null)
            claims.Add(new Claim(SsoEmailVerification.ClaimType, emailVerified));
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "SsoExternalCookie")), "SsoExternalCookie");

        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), "SsoExternalCookie"))
            .ReturnsAsync(AuthenticateResult.Success(ticket));
        auth.Setup(a => a.SignOutAsync(It.IsAny<HttpContext>(), "SsoExternalCookie", It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
        var controller = new AuthController(mediator.Object, Mock.Of<ISsoHandoffStore>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
        return (controller, auth);
    }
}
