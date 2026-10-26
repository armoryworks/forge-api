namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>Size limits of one stock load request.</summary>
public static class StockLoadLimits
{
    /// <summary>Most lines one load may carry.</summary>
    public const int MaxLines = 10000;

    /// <summary>Longest CSV text accepted in one request (characters).</summary>
    public const int MaxCsvLength = 2_000_000;
}
