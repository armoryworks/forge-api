using FluentAssertions;

using Forge.Api.Services;

namespace Forge.Tests.Services;

public class OpaqueTokensTests
{
    [Fact]
    public void NewToken_is_url_safe_and_unique()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => OpaqueTokens.NewToken()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
        foreach (var token in tokens)
        {
            token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        }
    }

    [Fact]
    public void Sha256Hex_is_deterministic_lowercase_hex()
    {
        var raw = OpaqueTokens.NewToken();

        var first = OpaqueTokens.Sha256Hex(raw);
        var second = OpaqueTokens.Sha256Hex(raw);

        first.Should().Be(second);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
        OpaqueTokens.Sha256Hex(raw + "x").Should().NotBe(first);
    }
}
