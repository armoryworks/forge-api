namespace Forge.Core.Models;

/// <summary>
/// BE-4 — edit a recurring order in place (was delete+recreate only). PATCH-style under
/// PUT: a null scalar leaves that field unchanged. <see cref="Lines"/> is tri-state —
/// null leaves the line set untouched; a non-null list replaces it wholesale (mirrors the
/// training-path module-set replace). Reuses <see cref="CreateRecurringOrderLineModel"/>.
/// </summary>
public record UpdateRecurringOrderRequestModel(
    string? Name,
    int? ShippingAddressId,
    int? IntervalDays,
    DateTimeOffset? NextGenerationDate,
    string? Notes,
    bool? IsActive,
    List<CreateRecurringOrderLineModel>? Lines);
