namespace Forge.Core.Entities;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — one line of a <see cref="VendorCreditMemo"/>. A line naming a bill line credits back that
/// line's account and can't exceed the billed quantity or price; a line naming a return line clears the vendor
/// returns clearing account; any other line credits its own determination key or GL account.
/// </summary>
public class VendorCreditMemoLine : BaseEntity
{
    public int VendorCreditMemoId { get; set; }

    public int LineNumber { get; set; }

    /// <summary>The bill line credited. Credited quantity per bill line never exceeds the billed quantity.</summary>
    public int? VendorBillLineId { get; set; }

    /// <summary>The return to vendor line this line credits.</summary>
    public int? VendorReturnLineId { get; set; }

    public int? PartId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>The determination key the issue posting credits. Default OPERATING_EXPENSE.</summary>
    public string AccountDeterminationKey { get; set; } = "OPERATING_EXPENSE";

    /// <summary>An explicit GL account the line credits; wins over <see cref="AccountDeterminationKey"/>.</summary>
    public int? GlAccountId { get; set; }

    public decimal LineTotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    public VendorCreditMemo VendorCreditMemo { get; set; } = null!;
    public VendorBillLine? VendorBillLine { get; set; }
    public VendorReturnLine? VendorReturnLine { get; set; }
    public Part? Part { get; set; }
}
