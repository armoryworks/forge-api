namespace Forge.Core.Enums;

/// <summary>
/// NACHA receiving-account type — drives the entry-detail transaction code
/// (checking credit 22 / savings credit 32; prenote variants 23 / 33).
/// </summary>
public enum BankAccountType
{
    Checking,
    Savings,
}
