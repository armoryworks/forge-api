using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateRecurringExpenseRequestModel(
    decimal? Amount,
    string? Category,
    string? Classification,
    string? Description,
    string? Vendor,
    RecurrenceFrequency? Frequency,
    DateTimeOffset? EndDate,
    bool? IsActive,
    bool? AutoApprove);
