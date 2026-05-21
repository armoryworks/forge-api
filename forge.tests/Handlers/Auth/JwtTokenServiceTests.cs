using FluentAssertions;
using Microsoft.Extensions.Configuration;

using Forge.Api.Services;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-053 — the JWT signing path has NO committed fallback key. Token issuance
/// must hard-fail when Jwt:Key is not configured (absent), so a deployment can
/// never silently sign with a default. (Empty/short keys are additionally
/// rejected at startup by the Program.cs guard.)
/// </summary>
public class JwtTokenServiceTests
{
    private static JwtTokenService Service(string? key)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "forge",
            ["Jwt:Audience"] = "forge-ui",
        };
        if (key is not null)
            dict["Jwt:Key"] = key;

        return new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(dict).Build());
    }

    [Fact]
    public void GenerateToken_WithNoJwtKey_Throws()
    {
        var act = () => Service(null).GenerateToken(
            1, "u@forge.local", "U", "Ser", "US", "#000", new List<string> { "Admin" });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenerateToken_WithValidKey_Succeeds()
    {
        var result = Service("a-valid-signing-key-of-at-least-32-characters!!").GenerateToken(
            1, "u@forge.local", "U", "Ser", "US", "#000", new List<string> { "Admin" });

        result.Token.Should().NotBeNullOrEmpty();
    }
}
