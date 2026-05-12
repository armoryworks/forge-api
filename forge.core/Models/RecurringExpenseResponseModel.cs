using Forge.Core.Enums;

namespace Forge.Core.Models;

public record RecurringExpenseResponseModel(
    int Id,
    int UserId,
    string UserName,
    decimal Amount,
    string Category,
    string Classification,
    string Description,
    string? Vendor,
    RecurrenceFrequency Frequency,
    DateTimeOffset NextOccurrenceDate,
    DateTimeOffset? LastGeneratedDate,
    DateTimeOffset? EndDate,
    bool IsActive,
    bool AutoApprove,
    DateTimeOffset CreatedAt);
