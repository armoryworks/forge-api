using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-054 — the MFA challenge gate (H-017 D1 + subject binding). A challenge can
/// only be created with a valid MFA-pending token (proof the password step
/// passed), and the userId is derived from that token — never caller-supplied.
/// </summary>
public class CreateMfaChallengeHandlerTests
{
    private readonly Mock<IMfaService> _mfaService = new();
    private readonly IMfaPreAuthTokenService _preAuth = new MfaPreAuthTokenService(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-at-least-32-chars-long!!",
                ["Jwt:Issuer"] = "forge",
            })
            .Build());

    private CreateMfaChallengeHandler Handler() => new(_mfaService.Object, _preAuth);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task Handle_InvalidOrMissingPreAuthToken_Throws_AndCreatesNoChallenge(string token)
    {
        var act = () => Handler().Handle(new CreateMfaChallengeCommand(token), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        // The bypass closure: no valid first-factor proof → no challenge is ever
        // created, so no challengeToken (and thus no full JWT) can be obtained.
        _mfaService.Verify(x => x.CreateChallengeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidPreAuthToken_CreatesChallengeForBoundUserOnly()
    {
        var token = _preAuth.Issue(5);
        _mfaService.Setup(x => x.CreateChallengeAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaChallengeResponseModel { ChallengeToken = "challenge-xyz" });

        var result = await Handler().Handle(new CreateMfaChallengeCommand(token), CancellationToken.None);

        result.ChallengeToken.Should().Be("challenge-xyz");
        _mfaService.Verify(x => x.CreateChallengeAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _mfaService.Verify(
            x => x.CreateChallengeAsync(It.Is<int>(id => id != 5), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
