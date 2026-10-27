namespace Forge.Core.Enums;

/// <summary>Per-bill status inside a payment run. Stored as an integer.</summary>
public enum PaymentRunItemStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
}
