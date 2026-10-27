namespace Forge.Core.Entities;

/// <summary>An open vendor credit memo a <see cref="PaymentRun"/> deducts from one of its bills.</summary>
public class PaymentRunCredit : BaseEntity
{
    public int PaymentRunId { get; set; }

    public int PaymentRunItemId { get; set; }

    public int VendorCreditMemoId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>The memo application the deduction became, once its vendor was paid.</summary>
    public int? VendorCreditMemoApplicationId { get; set; }

    public PaymentRun PaymentRun { get; set; } = null!;
    public PaymentRunItem PaymentRunItem { get; set; } = null!;
    public VendorCreditMemo VendorCreditMemo { get; set; } = null!;
}
