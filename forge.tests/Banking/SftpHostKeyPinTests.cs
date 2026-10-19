using FluentAssertions;

using Forge.Api.Features.Banking;
using Forge.Core.Settings;

namespace Forge.Tests.Banking;

/// <summary>
/// The bank SFTP channel trusts a server only when its SHA256 host key equals the pinned
/// fingerprint. The pin is accepted as ssh-keygen prints it (SHA256: prefix) or bare, padded or not;
/// a blank pin trusts nothing.
/// </summary>
public class SftpHostKeyPinTests
{
    private const string Presented = "ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og";

    [Theory]
    [InlineData("SHA256:ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og")]
    [InlineData("ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og")]
    [InlineData("  sha256:ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og=  ")]
    public void Matches_ThePinnedKeyInAnyPrintedForm(string pinned)
        => SftpHostKeyPin.Matches(pinned, Presented).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SHA256:")]
    public void NoPin_TrustsNoKey(string? pinned)
        => SftpHostKeyPin.Matches(pinned, Presented).Should().BeFalse();

    [Fact]
    public void ADifferentKey_IsRefused()
        => SftpHostKeyPin.Matches("SHA256:AAAAVZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og", Presented).Should().BeFalse();

    [Fact]
    public void Comparison_IsCaseSensitive()
        => SftpHostKeyPin.Matches(Presented.ToLowerInvariant(), Presented).Should().BeFalse();

    [Theory]
    [InlineData("SHA256:ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og", true)]
    [InlineData("ohD8VZEXGWo6Ez8GSEJQ9WpafgLFsOfLOtGGQCQo6Og=", true)]
    [InlineData("MD5:16:27:ac:a5:76:28:2d:36:63:1b:56:4d:eb:df:a6:48", false)]
    public void Descriptor_AcceptsOnlyASha256Fingerprint(string value, bool valid)
    {
        var descriptor = BankingSettings.Descriptors.Single(d => d.Key == BankingSettings.SftpHostKeyFingerprintKey);
        System.Text.RegularExpressions.Regex.IsMatch(value, descriptor.ValidationPattern!).Should().Be(valid);
    }
}
