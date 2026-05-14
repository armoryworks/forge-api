
namespace Forge.Core.Models;

public record CreateRecurringExpenseRequestModel(
    decimal Amount,
    string Category,
    string Classification,
    string Description,
    string? Vendor,
    RecurrenceFrequency Frequency,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    bool AutoApprove);
