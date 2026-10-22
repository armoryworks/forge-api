using System.Globalization;

namespace Forge.Api.Services;

/// <summary>
/// Formats amounts in one currency: the symbol before the amount, and the ISO code after it when the
/// currency is not the base currency or has no symbol (<c>$10,200.00</c>, <c>$10,200.00 CAD</c>,
/// <c>10,200.00 CHF</c>). Built by <see cref="MoneyFormat"/>.
/// </summary>
public sealed class MoneyFormatter
{
    private readonly string? _symbol;
    private readonly int _decimalPlaces;
    private readonly bool _showCode;

    internal MoneyFormatter(string code, string? symbol, int decimalPlaces, bool showCode)
    {
        Code = code;
        _symbol = symbol;
        _decimalPlaces = Math.Clamp(decimalPlaces, 0, 8);
        _showCode = showCode || symbol is null;
    }

    /// <summary>The ISO code of the currency.</summary>
    public string Code { get; }

    /// <summary>The same currency printed with <paramref name="decimalPlaces"/> decimals, e.g. for per-piece rates.</summary>
    public MoneyFormatter WithDecimalPlaces(int decimalPlaces) => new(Code, _symbol, decimalPlaces, _showCode);

    /// <summary>The same currency with its ISO code always printed after the amount.</summary>
    public MoneyFormatter WithCode() => new(Code, _symbol, _decimalPlaces, showCode: true);

    /// <summary>The amount in this currency, with a leading minus sign when negative.</summary>
    public string Format(decimal amount)
    {
        var rounded = Math.Round(amount, _decimalPlaces, MidpointRounding.AwayFromZero);
        var number = Math.Abs(rounded).ToString($"N{_decimalPlaces}", CultureInfo.InvariantCulture);
        var sign = rounded < 0 ? "-" : string.Empty;
        var suffix = _showCode ? $" {Code}" : string.Empty;
        return $"{sign}{_symbol}{number}{suffix}";
    }
}
