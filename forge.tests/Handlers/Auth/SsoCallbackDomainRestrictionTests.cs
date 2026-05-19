using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// Pinpoint tests for <c>SsoCallbackHandler.EnforceAllowedDomains</c>. The
/// domain check runs first thing in <c>Handle</c>, BEFORE any user lookup
/// — so we can exercise it without wiring up UserManager / TokenService /
/// SessionStore mocks. Bad-domain throws bubble out before any of the
/// other collaborators are touched.
/// </summary>
public class SsoCallbackDomainRestrictionTests
{
    private static SsoCallbackHandler MakeHandler(SsoOptions options)
    {
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        return new SsoCallbackHandler(
            userManagerMock.Object,
            Mock.Of<ITokenService>(),
            Mock.Of<ISessionStore>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IRoleClaimsExpander>(),
            Mock.Of<IOptionsMonitor<SsoOptions>>(m => m.CurrentValue == options));
    }

    [Fact]
    public async Task Handle_AllowedDomains_Empty_DoesNotRejectAnyDomain()
    {
        // No AllowedDomains configured → no restriction; the handler proceeds
        // to user lookup and ultimately throws InvalidOperationException
        // because the mocked UserManager finds nobody. The key assertion
        // here is that the throw is the "no account" one, NOT the
        // domain-rejection one.
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = null },
        });

        var act = () => handler.Handle(
            new SsoCallbackCommand("google", "ext-id-1", "anyone@whatever.example"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "empty AllowedDomains imposes no restriction; failure mode is the downstream 'no account' path");
    }

    [Fact]
    public async Task Handle_AllowedDomains_DomainPermitted_PassesEnforcement()
    {
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = new() { "example.com" } },
        });

        var act = () => handler.Handle(
            new SsoCallbackCommand("google", "ext-id-1", "jane@example.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "matching domain passes enforcement; failure mode is the downstream 'no account' path");
    }

    [Fact]
    public async Task Handle_AllowedDomains_DomainRejected_ThrowsSsoDomainNotPermitted()
    {
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = new() { "acme.example" } },
        });

        var act = () => handler.Handle(
            new SsoCallbackCommand("google", "ext-id-1", "outsider@gmail.com"),
            CancellationToken.None);

        var thrown = await act.Should()
            .ThrowAsync<SsoDomainNotPermittedException>();
        thrown.Which.Provider.Should().Be("google");
        thrown.Which.EmailDomain.Should().Be("gmail.com");
    }

    [Fact]
    public async Task Handle_AllowedDomains_CaseInsensitive_Match()
    {
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = new() { "Example.COM" } },
        });

        var act = () => handler.Handle(
            new SsoCallbackCommand("google", "ext-id-1", "jane@example.com"),
            CancellationToken.None);

        // Throws "no account" (downstream), NOT SsoDomainNotPermitted — proves
        // the case-insensitive comparison matched.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_AllowedDomains_Subdomain_NotAutoIncluded()
    {
        // example.com permits user@example.com but NOT user@sub.example.com.
        // Wildcards are deliberately unsupported.
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = new() { "example.com" } },
        });

        var act = () => handler.Handle(
            new SsoCallbackCommand("google", "ext-id-1", "jane@sub.example.com"),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<SsoDomainNotPermittedException>();
        thrown.Which.EmailDomain.Should().Be("sub.example.com");
    }

    [Fact]
    public async Task Handle_AllowedDomains_Per_Provider_Isolation()
    {
        // The Google list does NOT leak into Microsoft / OIDC enforcement
        // — each provider has its own AllowedDomains.
        var handler = MakeHandler(new SsoOptions
        {
            Google = new SsoProviderOptions { AllowedDomains = new() { "acme.example" } },
            Microsoft = new SsoProviderOptions { AllowedDomains = null },
        });

        // 'outsider@gmail.com' would be rejected on the Google path, but
        // the Microsoft path has no restriction, so it proceeds to the
        // downstream 'no account' throw.
        var act = () => handler.Handle(
            new SsoCallbackCommand("microsoft", "ext-id-1", "outsider@gmail.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
