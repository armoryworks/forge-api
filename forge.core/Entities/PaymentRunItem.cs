using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>A bill picked in a <see cref="PaymentRun"/>: the cash to pay, the discount to take and what became of it.</summary>
public class PaymentRunItem : BaseEntity
{
    public int PaymentRunId { get; set; }

    public int VendorBillId { get; set; }

    public int VendorId { get; set; }

    public int CurrencyId { get; set; }

    /// <summary>The cash picked for the bill, before vendor credit deductions.</summary>
    public decimal Amount { get; set; }

    /// <summary>The discount to take; null = the automatic early-payment discount.</summary>
    public decimal? DiscountAmount { get; set; }

    public PaymentRunItemStatus Status { get; set; } = PaymentRunItemStatus.Pending;

    public int? VendorPaymentId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public PaymentRun PaymentRun { get; set; } = null!;
    public VendorBill VendorBill { get; set; } = null!;
    public Vendor Vendor { get; set; } = null!;
    public ICollection<PaymentRunCredit> Credits { get; set; } = [];
}
