using System.Security.Cryptography;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Auth;

public class SetupActivationGuardTests
{
    private const string Salt = "0123456789abcdef0123456789abcdef";

    // Mirrors what the control plane writes into the tenant env.
    private static SetupActivationGuard GuardFor(string code)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + code));
        return new SetupActivationGuard(Options.Create(new SetupOptions
        {
            ActivationCodeHash = $"v1.{Salt}.{Convert.ToHexString(digest).ToLowerInvariant()}",
        }));
    }

    [Fact]
    public void UnconfiguredInstall_IsUngatedAndAcceptsAnything()
    {
        var guard = new SetupActivationGuard(Options.Create(new SetupOptions()));

        guard.IsRequired.Should().BeFalse();
        guard.Verify(null).Should().BeTrue();
        guard.Verify("whatever").Should().BeTrue();
    }

    [Fact]
    public void ConfiguredInstall_AcceptsTheIssuedCode()
    {
        var guard = GuardFor("K7M290XF3TQH");

        guard.IsRequired.Should().BeTrue();
        guard.Verify("K7M2-90XF-3TQH").Should().BeTrue();
    }

    [Theory]
    [InlineData("k7m2 90xf 3tqh")]      // lower case, spaces instead of hyphens
    [InlineData("K7M2-9OXF-3TQH")]      // letter O typed for zero
    [InlineData("K7M2-90XF-3TQH  ")]    // trailing whitespace
    public void TranscriptionSlips_StillValidate(string typed)
    {
        GuardFor("K7M290XF3TQH").Verify(typed).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7M2-90XF-3TQX")]
    public void WrongOrMissingCode_IsRejected(string? typed)
    {
        GuardFor("K7M290XF3TQH").Verify(typed).Should().BeFalse();
    }

    // ── Cross-repo contract ──────────────────────────────────────────────────
    // This verifier was produced by Tuyere's OWN implementation
    // (Tuyere.Core.Fleet.ActivationCode.Hash — a different repo sharing no code with
    // this one), which pins the identical vector on its side. If the two ever drift,
    // every provisioned tenant silently refuses the code its operator is reading out.
    [Fact]
    public void AcceptsACodeHashedByTheControlPlane()
    {
        var guard = new SetupActivationGuard(Options.Create(new SetupOptions
        {
            ActivationCodeHash =
                "v1.0123456789abcdef0123456789abcdef.91c7297cf6b21dd54a98562cb4b3bded03c3f53c5ca51a6b8f26b172a6929a9b",
        }));

        Assert.True(guard.Verify("K7M2-90XF-3TQH"));
        Assert.False(guard.Verify("K7M2-90XF-3TQX"));
    }

    [Fact]
    public void MalformedVerifier_RejectsEverythingRatherThanOpeningTheGate()
    {
        var guard = new SetupActivationGuard(Options.Create(new SetupOptions
        {
            ActivationCodeHash = "not-a-verifier",
        }));

        guard.IsRequired.Should().BeTrue();
        guard.Verify("anything").Should().BeFalse();
    }
}
