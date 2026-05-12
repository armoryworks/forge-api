namespace Forge.Core.Models;

public record CreateRecurringOrderRequestModel(
    string Name,
    int CustomerId,
    int? ShippingAddressId,
    int IntervalDays,
    DateTimeOffset NextGenerationDate,
    string? Notes,
    List<CreateRecurringOrderLineModel> Lines);

public record CreateRecurringOrderLineModel(
    int PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
