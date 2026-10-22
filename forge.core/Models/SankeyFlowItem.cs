namespace Forge.Core.Models;

public record SankeyFlowItem(string From, string To, decimal Flow, string? Unit = null)
{
    public const string CurrencyUnit = "currency";
    public const string CountUnit = "count";
}
