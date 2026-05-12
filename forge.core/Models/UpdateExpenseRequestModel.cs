namespace Forge.Core.Models;

public record UpdateExpenseRequestModel(
    decimal Amount,
    string Category,
    string Description,
    int? JobId,
    string? ReceiptFileId,
    DateTimeOffset ExpenseDate);
