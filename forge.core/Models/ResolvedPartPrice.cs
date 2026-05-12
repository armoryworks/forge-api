namespace Forge.Core.Models;

/// <summary>
/// Result of <see cref="Forge.Core.Interfaces.IPartPricingResolver.ResolveAsync"/>.
/// Carries the resolved unit price plus enough provenance to render a
/// "where did this come from?" hint in the UI.
/// </summary>
/// <param name="PartId">Part the price was resolved for.</param>
/// <param name="UnitPrice">Effective unit price. <c>0m</c> when <see cref="Source"/> is <see cref="PartPriceSource.Default"/>.</param>
/// <param name="Currency">ISO-4217 currency code (e.g. 'USD'). All four rungs now carry per-record currency (PriceListEntry + PartPrice in Dispatch B; VendorPartPriceTier already had it). The Default rung returns the install's chosen default 'USD'.</param>
/// <param name="Source">Which rung produced this result.</param>
/// <param name="SourceRowId">Row id of the producing record (PriceListEntryId / PartPriceId / VendorPartPriceTierId). Null when <see cref="Source"/> is <see cref="PartPriceSource.Default"/>.</param>
/// <param name="Notes">Optional notes from the producing row (price-change rationale, etc.).</param>
public record ResolvedPartPrice(
    int PartId,
    decimal UnitPrice,
    string Currency,
    PartPriceSource Source,
    int? SourceRowId,
    string? Notes);
