using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class RecurringExpense : BaseAuditableEntity
{
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public DateTimeOffset NextOccurrenceDate { get; set; }
    public DateTimeOffset? LastGeneratedDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoApprove { get; set; }
}
