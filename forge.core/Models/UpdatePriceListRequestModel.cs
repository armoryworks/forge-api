namespace Forge.Core.Models;

/// <summary>
/// Body for <c>PUT /api/v1/price-lists/{id}</c>.
///
/// CustomerId is intentionally omitted — a list's scope (customer-specific
/// vs. system-wide) is fixed at creation time. Move-between-customers is
/// not a supported operation; admins can recreate the list under the new
/// scope and re-import entries if needed.
/// </summary>
public record UpdatePriceListRequestModel(
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);
