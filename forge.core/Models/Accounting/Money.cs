namespace Forge.Core.Models.Accounting;

/// <summary>
/// Commercial money rounding shared by every document and posting: 2 dp, half away from zero
/// (the F-027 quantize behind <c>Invoice.BalanceDue</c>), NOT .NET's default banker's rounding,
/// which diverges on half-cents. Unit prices, costs and quantities keep up to 4 dp; only amounts
/// of money go through <see cref="Round"/>.
/// </summary>
public static class Money
{
    /// <summary>Decimal places of an amount of money.</summary>
    public const int Scale = 2;

    /// <summary>Decimal places of a unit price, unit cost or quantity.</summary>
    public const int UnitScale = 4;

    /// <summary>Rounds an amount to 2 dp, half away from zero.</summary>
    public static decimal Round(decimal value)
        => Math.Round(value, Scale, MidpointRounding.AwayFromZero);
}
