using FluentAssertions;

using Forge.Core.Enums;

namespace Forge.Tests.Handlers.Invoices;

public class CreditTermsExtensionsTests
{
    [Theory]
    [InlineData("DueOnReceipt", CreditTerms.DueOnReceipt)]
    [InlineData("Due on receipt", CreditTerms.DueOnReceipt)]
    [InlineData("Net15", CreditTerms.Net15)]
    [InlineData("NET-30", CreditTerms.Net30)]
    [InlineData("net 45", CreditTerms.Net45)]
    [InlineData("Net60", CreditTerms.Net60)]
    [InlineData("Net 90", CreditTerms.Net90)]
    public void Lenient_parse_reads_names_and_typed_spellings(string value, CreditTerms expected)
    {
        CreditTermsExtensions.TryParseLenient(value, out var terms).Should().BeTrue();
        terms.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("Net 31")]
    public void Lenient_parse_rejects_numbers_and_unknown_terms(string? value)
    {
        CreditTermsExtensions.TryParseLenient(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Net_days_and_from_net_days_round_trip()
    {
        foreach (var terms in Enum.GetValues<CreditTerms>())
            CreditTermsExtensions.FromNetDays(terms.NetDays()).Should().Be(terms);

        CreditTermsExtensions.FromNetDays(37).Should().BeNull();
    }
}
