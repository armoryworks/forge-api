namespace Forge.Core.Enums;

/// <summary>Why a credit memo was issued. Stored as an integer.</summary>
public enum CreditMemoReasonType
{
    Return = 0,
    PriceAdjustment = 1,
    Goodwill = 2,
    WriteOff = 3,
    Other = 4,
}
