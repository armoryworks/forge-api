using System.Security.Authentication;

using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Auth;

public class ExchangeSsoTokenHandlerTests
{
    private readonly Mock<IExternalIdTokenValidator> _validator = new();
    private readonly Mock<IMediator> _mediator = new();

    private ExchangeSsoTokenHandler MakeHandler() =>
        new(_validator.Object, _mediator.Object);

    private static LoginResponse SampleResponse(string email = "jane@example.com") =>
        new("forge-jwt",
            DateTimeOffset.UtcNow.AddHours(1),
            new AuthUserResponseModel(7, email, "Jane", "Doe", "JD", "#0d9488",
                new[] { "Engineer" }, false));

    // ── Dispatch — every supported provider routes to its own validator ─────

    [Fact]
    public async Task Handle_GoogleProvider_RoutesToValidateGoogle_AndForwardsCallback()
    {
        _validator
            .Setup(v => v.ValidateGoogleAsync("good-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims(
                Subject: "google-sub-abc",
                Email: "jane@example.com",
                HostedDomain: "example.com"));

        var expected = SampleResponse();
        _mediator
            .Setup(m => m.Send(
                It.Is<SsoCallbackCommand>(c =>
                    c.Provider == "google"
                    && c.ExternalId == "google-sub-abc"
                    && c.Email == "jane@example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await MakeHandler().Handle(
            new ExchangeSsoTokenCommand("google", "good-token"),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        _validator.Verify(v => v.ValidateMicrosoftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _validator.Verify(v => v.ValidateOidcAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MicrosoftProvider_RoutesToValidateMicrosoft_AndForwardsCallback()
    {
        _validator
            .Setup(v => v.ValidateMicrosoftAsync("ms-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims(
                Subject: "aad-oid-7e9c", // <- the AAD object id, not `sub`
                Email: "jane@contoso.com",
                HostedDomain: null));

        var expected = SampleResponse("jane@contoso.com");
        _mediator
            .Setup(m => m.Send(
                It.Is<SsoCallbackCommand>(c =>
                    c.Provider == "microsoft"
                    && c.ExternalId == "aad-oid-7e9c"
                    && c.Email == "jane@contoso.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await MakeHandler().Handle(
            new ExchangeSsoTokenCommand("microsoft", "ms-token"),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        _validator.Verify(v => v.ValidateGoogleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OidcProvider_RoutesToValidateOidc_AndForwardsCallback()
    {
        _validator
            .Setup(v => v.ValidateOidcAsync("oidc-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims(
                Subject: "okta-sub-12345",
                Email: "jane@example.com",
                HostedDomain: null));

        var expected = SampleResponse();
        _mediator
            .Setup(m => m.Send(
                It.Is<SsoCallbackCommand>(c =>
                    c.Provider == "oidc"
                    && c.ExternalId == "okta-sub-12345"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await MakeHandler().Handle(
            new ExchangeSsoTokenCommand("oidc", "oidc-token"),
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        _validator.Verify(v => v.ValidateMicrosoftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("MICROSOFT")]
    [InlineData("Oidc")]
    public async Task Handle_ProviderCasingIsNormalized(string casedProvider)
    {
        _validator.Setup(v => v.ValidateGoogleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims("s", "e@x.com", null));
        _validator.Setup(v => v.ValidateMicrosoftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims("s", "e@x.com", null));
        _validator.Setup(v => v.ValidateOidcAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims("s", "e@x.com", null));
        _mediator.Setup(m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleResponse());

        await MakeHandler().Handle(
            new ExchangeSsoTokenCommand(casedProvider, "long-enough-token"),
            CancellationToken.None);

        // The SsoCallbackCommand's Provider is normalized to lowercase so
        // SsoCallbackHandler's switch on it always matches.
        _mediator.Verify(m => m.Send(
            It.Is<SsoCallbackCommand>(c => c.Provider == casedProvider.ToLowerInvariant()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Error paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidatorRejects_PropagatesAuthenticationException()
    {
        _validator
            .Setup(v => v.ValidateGoogleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException("id_token expired"));

        var act = () => MakeHandler().Handle(
            new ExchangeSsoTokenCommand("google", "expired-token"),
            CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>().WithMessage("*expired*");

        _mediator.Verify(
            m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation failure must short-circuit BEFORE any downstream SsoCallback work");
    }

    [Fact]
    public async Task Handle_UnknownProvider_ThrowsAuthenticationException()
    {
        // Belt-and-suspenders: FluentValidation rejects unknown providers, but
        // the dispatch switch must also be total so an upstream bypass can't
        // reach SsoCallback with an unvalidated principal.
        var act = () => MakeHandler().Handle(
            new ExchangeSsoTokenCommand("facebook", "long-enough-token"),
            CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .WithMessage("*facebook*");
        _mediator.Verify(m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── FluentValidation surface ───────────────────────────────────────────

    [Fact]
    public void Validator_RejectsEmpty_Provider()
    {
        var result = new ExchangeSsoTokenValidator().Validate(
            new ExchangeSsoTokenCommand("", "token-token-token"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Provider");
    }

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("oidc")]
    [InlineData("Google")]    // case-insensitive
    [InlineData("MICROSOFT")]
    public void Validator_AcceptsSupportedProvider(string provider)
    {
        var result = new ExchangeSsoTokenValidator().Validate(
            new ExchangeSsoTokenCommand(provider, "long-enough-token-value"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("apple")]
    [InlineData("twitter")]
    public void Validator_RejectsUnsupportedProvider(string provider)
    {
        var result = new ExchangeSsoTokenValidator().Validate(
            new ExchangeSsoTokenCommand(provider, "long-enough-token-value"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Provider"
            && e.ErrorMessage.Contains("google", StringComparison.OrdinalIgnoreCase)
            && e.ErrorMessage.Contains("microsoft", StringComparison.OrdinalIgnoreCase)
            && e.ErrorMessage.Contains("oidc", StringComparison.OrdinalIgnoreCase));
    }
}
