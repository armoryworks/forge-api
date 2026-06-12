using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ExpenseResponseModel(
    int Id,
    int UserId,
    string UserName,
    int? JobId,
    string? JobNumber,
    decimal Amount,
    string Category,
    string Description,
    string? ReceiptFileId,
    ExpenseStatus Status,
    int? ApprovedBy,
    string? ApprovedByName,
    string? ApprovalNotes,
    DateTimeOffset ExpenseDate,
    DateTimeOffset CreatedAt,
    // Vendor-settled expenses: the vendor the expense is owed to, and the live (non-void) vendor
    // bill the approval promoted it into (the payable is paid/aged/voided through that bill).
    int? VendorId = null,
    string? VendorName = null,
    int? LinkedVendorBillId = null,
    string? LinkedVendorBillNumber = null);
