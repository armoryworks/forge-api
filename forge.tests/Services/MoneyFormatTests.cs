using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class MoneyFormatTests
{
    private static readonly Currency Usd = new() { Code = "USD", Symbol = "$", DecimalPlaces = 2 };
    private static readonly Currency Cad = new() { Code = "CAD", Symbol = "$", DecimalPlaces = 2 };
    private static readonly Currency Jpy = new() { Code = "JPY", Symbol = "¥", DecimalPlaces = 0 };
    private static readonly Currency Chf = new() { Code = "CHF", Symbol = "", DecimalPlaces = 2 };

    [Fact]
    public void BaseCurrency_PrintsSymbolOnly()
        => MoneyFormat.For(Usd, "USD").Format(10200m).Should().Be("$10,200.00");

    [Fact]
    public void ForeignCurrency_SharingTheBaseSymbol_AddsItsCode()
        => MoneyFormat.For(Cad, "USD").Format(10200m).Should().Be("$10,200.00 CAD");

    [Fact]
    public void CurrencyWithoutSymbol_PrintsItsCode()
        => MoneyFormat.For(Chf, "CHF").Format(5m).Should().Be("5.00 CHF");

    [Fact]
    public void DecimalPlacesComeFromTheCurrency()
        => MoneyFormat.For(Jpy, "USD").Format(1234.6m).Should().Be("¥1,235 JPY");

    [Fact]
    public void NegativeAmount_PutsTheSignFirst()
        => MoneyFormat.For(Usd, "USD").Format(-12.5m).Should().Be("-$12.50");

    [Fact]
    public void UnknownCurrency_FallsBackToTheBase()
        => MoneyFormat.For(null, "usd").Format(1m).Should().Be("$1.00");

    [Fact]
    public void WithDecimalPlaces_PrintsRatesAtTheirPrecision()
        => MoneyFormat.For(Usd, "USD").WithDecimalPlaces(4).Format(0.125m).Should().Be("$0.1250");

    [Fact]
    public void WithCode_PrintsTheBaseCode()
        => MoneyFormat.For(Usd, "USD").WithCode().Format(3m).Should().Be("$3.00 USD");

    [Fact]
    public void Output_NeverContainsTheGenericCurrencySign()
        => MoneyFormat.For(Cad, "USD").Format(1m).Should().NotContain("¤");

    [Theory]
    [InlineData("0.0725", "7.25%")]
    [InlineData("0.07", "7%")]
    [InlineData("0.123456", "12.3456%")]
    public void Percent_KeepsFractionalRates(string rate, string expected)
        => MoneyFormat.Percent(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void Percent_HonoursMaxDecimalPlaces()
        => MoneyFormat.Percent(0.12345m, 1).Should().Be("12.3%");

    [Theory]
    [InlineData("1940.0000", "1,940")]
    [InlineData("2.5000", "2.5")]
    [InlineData("0.1250", "0.125")]
    public void Quantity_GroupsAndTrimsTrailingZeros(string quantity, string expected)
        => MoneyFormat.Quantity(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public async Task BaseCodeAsync_ReadsTheCurrencyBaseSetting()
    {
        using var db = TestDbContextFactory.Create();
        db.SystemSettings.Add(new SystemSetting { Key = "currency.base", Value = " cad " });
        await db.SaveChangesAsync();

        (await MoneyFormat.BaseCodeAsync(db, CancellationToken.None)).Should().Be("CAD");
    }

    [Fact]
    public async Task BaseCodeAsync_DefaultsToUsd()
    {
        using var db = TestDbContextFactory.Create();

        (await MoneyFormat.BaseCodeAsync(db, CancellationToken.None)).Should().Be("USD");
    }

    [Fact]
    public async Task ForCurrencyIdAsync_AddsTheCodeOnlyForNonBaseCurrencies()
    {
        using var db = TestDbContextFactory.Create();
        db.Currencies.AddRange(
            new Currency { Code = "USD", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true },
            new Currency { Code = "CAD", Symbol = "$", DecimalPlaces = 2 });
        await db.SaveChangesAsync();
        var usdId = db.Currencies.Single(c => c.Code == "USD").Id;
        var cadId = db.Currencies.Single(c => c.Code == "CAD").Id;

        (await MoneyFormat.ForCurrencyIdAsync(db, usdId, CancellationToken.None)).Format(1m).Should().Be("$1.00");
        (await MoneyFormat.ForCurrencyIdAsync(db, cadId, CancellationToken.None)).Format(1m).Should().Be("$1.00 CAD");
    }

    [Fact]
    public async Task ForCurrencyIdsAsync_MixedCurrencies_PrintsTheBaseWithItsCode()
    {
        using var db = TestDbContextFactory.Create();
        db.Currencies.AddRange(
            new Currency { Code = "USD", Symbol = "$", DecimalPlaces = 2 },
            new Currency { Code = "CAD", Symbol = "$", DecimalPlaces = 2 });
        await db.SaveChangesAsync();
        var ids = db.Currencies.Select(c => c.Id).ToList();

        (await MoneyFormat.ForCurrencyIdsAsync(db, ids, CancellationToken.None)).Format(1m).Should().Be("$1.00 USD");
        (await MoneyFormat.ForCurrencyIdsAsync(db, [], CancellationToken.None)).Format(1m).Should().Be("$1.00");
    }
}
