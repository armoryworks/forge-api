
namespace Forge.Core.Models;

public record UpcomingExpenseResponseModel(
    int RecurringExpenseId,
    string Description,
    string Category,
    string Classification,
    string? Vendor,
    decimal Amount,
    DateTimeOffset DueDate,
    RecurrenceFrequency Frequency,
    bool AutoApprove);
