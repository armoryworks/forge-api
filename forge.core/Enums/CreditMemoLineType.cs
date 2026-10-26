namespace Forge.Core.Enums;

/// <summary>
/// What a credit memo line credits. Item lines credit Sales Returns and Allowances, Freight lines Freight Income,
/// WriteOff lines Bad Debt Expense; a RestockingFee line is kept back from the credit.
/// </summary>
public enum CreditMemoLineType
{
    Item = 0,
    Freight = 1,
    WriteOff = 2,
    RestockingFee = 3,
}
