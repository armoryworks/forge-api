
namespace Forge.Core.Models;

public record UpdateExpenseStatusRequestModel(
    ExpenseStatus Status,
    string? ApprovalNotes);
