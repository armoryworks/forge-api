namespace Forge.Core.Models;

public record RecurringOrderDetailResponseModel(
    int Id,
    string Name,
    int CustomerId,
    string CustomerName,
    int? ShippingAddressId,
    int IntervalDays,
    DateTimeOffset NextGenerationDate,
    DateTimeOffset? LastGeneratedDate,
    bool IsActive,
    string? Notes,
    List<RecurringOrderLineResponseModel> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
