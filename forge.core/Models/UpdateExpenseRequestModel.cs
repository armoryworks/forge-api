namespace Forge.Core.Models;

public record UpdateExpenseRequestModel(
    decimal Amount,
    string Category,
    string Description,
    int? JobId,
    string? ReceiptFileId,
    DateTimeOffset ExpenseDate,
    // Vendor-settled expenses: when a vendor is named, approval promotes the expense into a vendor
    // bill (Accounts Payable) so it is paid through the AP pipeline. Absent → out-of-pocket (cash).
    int? VendorId = null);
