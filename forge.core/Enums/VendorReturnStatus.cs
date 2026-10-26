namespace Forge.Core.Enums;

/// <summary>
/// Lifecycle of a return to vendor: Open when the NCR disposition creates it, Shipped once the held stock leaves,
/// Credited when the vendor's credit is recorded, Closed when its NCR is closed. Stored as an integer.
/// </summary>
public enum VendorReturnStatus
{
    Open = 0,
    Shipped = 1,
    Credited = 2,
    Closed = 3,
}
