namespace Forge.Core.Enums.Accounting;

/// <summary>
/// The side on which an account normally carries a positive balance. Asset and
/// Expense accounts are <see cref="Debit"/>; Liability, Equity, and Income
/// accounts are <see cref="Credit"/>.
/// </summary>
public enum NormalBalance
{
    Debit,
    Credit,
}
