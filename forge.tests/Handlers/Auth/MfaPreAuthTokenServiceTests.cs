using FluentAssertions;
using Microsoft.Extensions.Configuration;

using Forge.Api.Services;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-054 — unit coverage for the MFA-pending pre-auth token (H-017 §A).
/// Proves the token round-trips, is subject-bound, and is NOT interchangeable
/// with a full access JWT (the property that prevents the bypass once the MFA
/// endpoints require it).
/// </summary>
public class MfaPreAuthTokenServiceTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-at-least-32-chars-long!!",
                ["Jwt:Issuer"] = "forge",
                ["Jwt:Audience"] = "forge-ui",
            })
            .Build();

    private readonly MfaPreAuthTokenService _svc = new(Config());

    [Fact]
    public void Issue_ThenValidate_ReturnsSameUserId()
    {
        var token = _svc.Issue(42);
        _svc.ValidateAndGetUserId(token).Should().Be(42);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("aaa.bbb.ccc")]
    public void Validate_GarbageOrEmpty_ReturnsNull(string token)
    {
        _svc.ValidateAndGetUserId(token).Should().BeNull();
    }

    [Fact]
    public void Validate_TamperedToken_ReturnsNull()
    {
        var token = _svc.Issue(7);
        // Flip the last char of the signature segment.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');
        _svc.ValidateAndGetUserId(tampered).Should().BeNull();
    }

    [Fact]
    public void Validate_TokenSignedWithDifferentKey_ReturnsNull()
    {
        var other = new MfaPreAuthTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "a-completely-different-signing-key-32chars!!",
                ["Jwt:Issuer"] = "forge",
            })
            .Build());

        var foreignToken = other.Issue(7);
        _svc.ValidateAndGetUserId(foreignToken).Should().BeNull();
    }

    [Fact]
    public void Validate_FullAccessJwt_IsRejected()
    {
        // A normal access token (main key + "forge-ui" audience + role claims)
        // must NOT validate as an MFA-pending token — the derived signing key
        // and the purpose/audience guards keep the two token types disjoint.
        var fullToken = new JwtTokenService(Config()).GenerateToken(
            7, "u@forge.local", "U", "Ser", "US", "#000",
            new List<string> { "Admin" });

        _svc.ValidateAndGetUserId(fullToken.Token).Should().BeNull();
    }

    [Fact]
    public void Issue_BindsToTheCorrectUser()
    {
        var a = _svc.Issue(100);
        var b = _svc.Issue(200);

        _svc.ValidateAndGetUserId(a).Should().Be(100);
        _svc.ValidateAndGetUserId(b).Should().Be(200);
    }
}
