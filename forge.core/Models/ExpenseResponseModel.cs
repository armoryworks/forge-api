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
    DateTimeOffset CreatedAt);
