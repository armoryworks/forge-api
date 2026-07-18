using FluentAssertions;

using Forge.Api.Features.Barcodes;

namespace Forge.Tests.Handlers.Barcodes;

/// <summary>GS1 GTIN math — check digit, build-from-prefix, and validation. This is what makes a
/// self-issued code globally unique, so it has to be exactly right.</summary>
public class Gs1Tests
{
    [Theory]
    // Known-good real GTINs (last digit is the check digit).
    [InlineData("036000291452")] // UPC-A
    [InlineData("0012345678905")] // GTIN-13 form of a well-known example
    [InlineData("4006381333931")] // EAN-13 textbook example
    public void IsValidGtin_accepts_correct_check_digits(string gtin)
        => Gs1.IsValidGtin(gtin).Should().BeTrue();

    [Theory]
    [InlineData("036000291451")] // wrong check digit
    [InlineData("12345")]         // wrong length
    [InlineData("03600029145X")]  // non-digit
    [InlineData("")]
    [InlineData(null)]
    public void IsValidGtin_rejects_bad_input(string? gtin)
        => Gs1.IsValidGtin(gtin).Should().BeFalse();

    [Fact]
    public void CalculateCheckDigit_matches_known_value()
        // Body "03600029145" → check digit 2 (i.e. full UPC-A 036000291452).
        => Gs1.CalculateCheckDigit("03600029145").Should().Be(2);

    [Fact]
    public void BuildGtin13_composes_prefix_item_and_check_digit()
    {
        // 7-digit prefix + 5-digit item ref = 12 digits + check.
        var gtin = Gs1.BuildGtin13("0614141", 8);
        gtin.Should().HaveLength(13);
        gtin.Should().StartWith("0614141");           // prefix
        gtin.Substring(7, 5).Should().Be("00008");    // zero-padded item reference
        Gs1.IsValidGtin(gtin).Should().BeTrue();       // self-consistent check digit
    }

    [Fact]
    public void BuildGtin13_is_deterministic_and_unique_per_item_ref()
    {
        var a = Gs1.BuildGtin13("0614141", 1);
        var b = Gs1.BuildGtin13("0614141", 2);
        a.Should().NotBe(b);
        Gs1.BuildGtin13("0614141", 1).Should().Be(a);
    }

    [Fact]
    public void BuildGtin13_rejects_out_of_range_prefix_and_exhausted_capacity()
    {
        var tooShort = () => Gs1.BuildGtin13("12345", 1);      // < 6 digits
        tooShort.Should().Throw<ArgumentException>();

        // 11-digit prefix leaves a single item-reference digit (0-9); 10 overflows.
        var overflow = () => Gs1.BuildGtin13("06141410000", 10);
        overflow.Should().Throw<InvalidOperationException>();
    }
}
