namespace Forge.Core.Enums;

/// <summary>Why a vendor credit or debit memo exists. Stored as an integer.</summary>
public enum VendorCreditMemoSourceType
{
    Return = 0,
    PriceAdjustment = 1,
    Rebate = 2,
    Other = 3,
}
