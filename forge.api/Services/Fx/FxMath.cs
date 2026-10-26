namespace Forge.Api.Services.Fx;

/// <summary>Conversion of document-currency amounts to the functional currency.</summary>
public static class FxMath
{
    /// <summary>
    /// <paramref name="amount"/> in the functional currency at <paramref name="fxRate"/>, rounded half away from
    /// zero to the cent. At rate 1 the amount is returned unrounded, exactly as a single-currency document
    /// always posted.
    /// </summary>
    public static decimal ToFunctional(decimal amount, decimal fxRate)
        => fxRate == 1m ? amount : Math.Round(amount * fxRate, 2, MidpointRounding.AwayFromZero);
}
