using System.Security.Cryptography;
using System.Text;

using FluentAssertions;

using Forge.Api.Features.Admin;

namespace Forge.Tests.Handlers.Auth;

/// <summary>
/// F-052 — setup tokens are stored hashed (SHA-256), mirroring the portal
/// magic-link flow (<c>PortalAuthService.HashToken</c>). The plaintext token is
/// returned to the caller once and never persisted; redemption hashes the
/// submitted token and compares against the stored hash.
/// </summary>
public class SetupTokenHashingTests
{
    [Fact]
    public void HashSetupToken_NeverReturnsThePlaintext()
    {
        const string token = "ABCD-2345";
        var stored = CreateAdminUserHandler.HashSetupToken(token);

        // What gets persisted must never be the plaintext token.
        stored.Should().NotBe(token);
        stored.Should().NotContain(token);
        // It is a SHA-256 hex digest (64 uppercase hex chars).
        stored.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void HashSetupToken_IsDeterministic_NormalizingCaseAndWhitespace()
    {
        // issue → verify success: hashing the redeemed token reproduces the
        // stored hash, after trim + upper-invariant normalization.
        var stored = CreateAdminUserHandler.HashSetupToken("ABCD-2345");
        CreateAdminUserHandler.HashSetupToken("  abcd-2345 ").Should().Be(stored);
    }

    [Fact]
    public void HashSetupToken_DifferentToken_ProducesDifferentHash()
    {
        // tampered / wrong token → different hash → redemption lookup fails.
        var a = CreateAdminUserHandler.HashSetupToken("ABCD-2345");
        var b = CreateAdminUserHandler.HashSetupToken("ABCD-2346");
        a.Should().NotBe(b);
    }

    [Fact]
    public void HashSetupToken_MatchesSha256OfNormalizedToken()
    {
        const string token = "WXYZ-7890";
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().ToUpperInvariant())));

        CreateAdminUserHandler.HashSetupToken(token).Should().Be(expected);
    }
}
