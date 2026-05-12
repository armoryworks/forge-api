namespace Forge.Core.Models;

public record RecurringOrderListItemModel(
    int Id,
    string Name,
    int CustomerId,
    string CustomerName,
    int IntervalDays,
    DateTimeOffset NextGenerationDate,
    DateTimeOffset? LastGeneratedDate,
    bool IsActive,
    int LineCount,
    DateTimeOffset CreatedAt);
