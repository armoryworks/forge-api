using System.Security.Claims;

using FluentAssertions;

using Forge.Api.Extensions;

namespace Forge.Tests.Authentication;

public class ClaimsPrincipalDisplayNameTests
{
    private static ClaimsPrincipal With(params (string type, string value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "Test"));

    [Fact]
    public void DisplayName_PrefersTheNameClaimThatApiKeysCarry()
    {
        With((ClaimTypes.Name, "Scanner Key"), (ClaimTypes.GivenName, "Avery"))
            .DisplayName().Should().Be("Scanner Key");
    }

    [Fact]
    public void DisplayName_BuildsTheNameFromAUserSignInsGivenNameAndSurname()
    {
        With((ClaimTypes.GivenName, "Avery"), (ClaimTypes.Surname, "Stone"), (ClaimTypes.Email, "avery@example.test"))
            .DisplayName().Should().Be("Avery Stone");
    }

    [Fact]
    public void DisplayName_FallsBackToEmailThenUnknown()
    {
        With((ClaimTypes.Email, "avery@example.test")).DisplayName().Should().Be("avery@example.test");
        With().DisplayName().Should().Be("Unknown");
        ((ClaimsPrincipal?)null).DisplayName().Should().Be("Unknown");
    }
}
