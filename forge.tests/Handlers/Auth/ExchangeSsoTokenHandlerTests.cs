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

    [Fact]
    public async Task Handle_ValidToken_ForwardsToSsoCallbackCommand_AndReturnsLoginResponse()
    {
        _validator
            .Setup(v => v.ValidateGoogleAsync("good-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalIdTokenClaims(
                Subject: "google-sub-abc",
                Email: "jane@example.com",
                HostedDomain: "example.com"));

        var expected = new LoginResponse(
            "forge-jwt",
            DateTimeOffset.UtcNow.AddHours(1),
            new AuthUserResponseModel(7, "jane@example.com", "Jane", "Doe", "JD", "#0d9488",
                new[] { "Engineer" }, false));

        _mediator
            .Setup(m => m.Send(
                It.Is<SsoCallbackCommand>(c =>
                    c.Provider == "google"
                    && c.ExternalId == "google-sub-abc"
                    && c.Email == "jane@example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = MakeHandler();
        var result = await handler.Handle(
            new ExchangeSsoTokenCommand("google", "good-token"),
            CancellationToken.None);

        result.Should().BeSameAs(expected);

        _mediator.Verify(
            m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the token-exchange handler must delegate user resolution + JWT issuance " +
            "to the shared SsoCallbackHandler — never duplicate that logic");
    }

    [Fact]
    public async Task Handle_ValidatorRejects_PropagatesAuthenticationException()
    {
        _validator
            .Setup(v => v.ValidateGoogleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException("id_token expired"));

        var handler = MakeHandler();
        var act = () => handler.Handle(
            new ExchangeSsoTokenCommand("google", "expired-token"),
            CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .WithMessage("*expired*");

        _mediator.Verify(
            m => m.Send(It.IsAny<SsoCallbackCommand>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation failure must short-circuit BEFORE any downstream SsoCallback work");
    }

    [Fact]
    public void Validator_RejectsEmpty_Provider()
    {
        // FluentValidation-level assertion: this is the validator wired up
        // via the MediatR pipeline. We exercise the class directly here
        // rather than going through MediatR.
        var validator = new ExchangeSsoTokenValidator();

        var result = validator.Validate(new ExchangeSsoTokenCommand("", "token-token-token"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Provider");
    }

    [Fact]
    public void Validator_RejectsNonGoogle_Provider()
    {
        // Defensive: today only Google is wired in the validator service.
        // Adding 'microsoft' / 'oidc' is a future change; the FluentValidator
        // is the gate that prevents silent broken calls until then.
        var validator = new ExchangeSsoTokenValidator();

        var result = validator.Validate(new ExchangeSsoTokenCommand("microsoft", "token-token-token"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "Provider"
            && e.ErrorMessage.Contains("google", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_AcceptsValid_Request()
    {
        var validator = new ExchangeSsoTokenValidator();

        var result = validator.Validate(
            new ExchangeSsoTokenCommand("google", "long-enough-token-value"));

        result.IsValid.Should().BeTrue();
    }
}
