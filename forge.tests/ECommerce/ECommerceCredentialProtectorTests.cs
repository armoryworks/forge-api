using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;

namespace Forge.Tests.ECommerce;

/// <summary>
/// The column has always been named <c>EncryptedCredentials</c>; until this seam
/// landed, nothing made that true — both handlers assigned caller plaintext
/// straight onto it. These tests pin the behaviour that makes the name honest.
/// </summary>
public class ECommerceCredentialProtectorTests
{
    private static ECommerceCredentialProtector CreateProtector() =>
        new(DataProtectionProvider.Create(nameof(ECommerceCredentialProtectorTests)),
            NullLogger<ECommerceCredentialProtector>.Instance);

    [Fact]
    public void Protect_DoesNotReturnThePlaintext()
    {
        var protector = CreateProtector();
        const string token = "shpat_realstoreadmintoken";

        var protectedValue = protector.Protect(token);

        Assert.NotNull(protectedValue);
        Assert.NotEqual(token, protectedValue);
        Assert.DoesNotContain(token, protectedValue);
    }

    [Fact]
    public void Unprotect_RoundTripsWhatProtectWrote()
    {
        var protector = CreateProtector();
        const string token = "ck_abc123:cs_def456";

        var roundTripped = protector.Unprotect(protector.Protect(token));

        Assert.Equal(token, roundTripped);
    }

    [Fact]
    public void Unprotect_PassesThroughLegacyPlaintext()
    {
        // Rows written before the seam existed hold raw credentials. Throwing on
        // those would break every poll on an upgraded install; the value
        // re-protects on the integration's next save.
        var protector = CreateProtector();
        const string legacy = "plain-token-written-before-the-seam";

        Assert.Equal(legacy, protector.Unprotect(legacy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProtectAndUnprotect_TreatBlankAsNull(string? blank)
    {
        var protector = CreateProtector();

        Assert.Null(protector.Protect(blank));
        Assert.Null(protector.Unprotect(blank));
    }

    [Fact]
    public void Protect_IsScopedToItsOwnPurpose()
    {
        // A different domain's protector must not be able to read e-commerce
        // credentials — that isolation is the point of a per-purpose seam.
        var provider = DataProtectionProvider.Create(nameof(ECommerceCredentialProtectorTests));
        var ecommerce = new ECommerceCredentialProtector(
            provider, NullLogger<ECommerceCredentialProtector>.Instance);
        var banking = provider.CreateProtector("Forge.Banking");

        var protectedValue = ecommerce.Protect("shpat_token")!;

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => banking.Unprotect(protectedValue));
    }
}
