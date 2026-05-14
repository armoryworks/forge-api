
namespace Forge.Core.Entities;

public class Expense : BaseAuditableEntity
{
    public int UserId { get; set; }
    public int? JobId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReceiptFileId { get; set; }
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
    public int? ApprovedBy { get; set; }
    public string? ApprovalNotes { get; set; }
    public string? ExternalExpenseId { get; set; }

    // Standardized accounting integration fields
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    public DateTimeOffset ExpenseDate { get; set; }

    public Job? Job { get; set; }
}
