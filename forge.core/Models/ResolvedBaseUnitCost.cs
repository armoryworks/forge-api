namespace Forge.Core.Models;

/// <summary>
/// UoM purchase-units effort — the cost of one **base/stock unit** of a part, derived from the
/// preferred vendor's price tier divided by the chosen purchase unit's content quantity
/// (e.g. $50 per 4×8 sheet ÷ 32 sqft = $6.25/sqft; $12 per bag ÷ 100 ea = $0.12/ea).
/// </summary>
/// <param name="PartId">The part this cost is for.</param>
/// <param name="CostPerBaseUnit">Cost of one base/stock UoM (tier price ÷ option content).</param>
/// <param name="Currency">ISO-4217 currency of the source tier.</param>
/// <param name="PurchaseUnitId">Which purchase unit was used; null = a per-base-unit tier (legacy single option).</param>
/// <param name="OptionContentQuantity">The divisor applied (the option's content in base UoM; 1 when no option).</param>
/// <param name="TierUnitPrice">The source tier's price for one of the option.</param>
/// <param name="TierId">The source <c>VendorPartPriceTier</c> row; null when nothing resolved.</param>
/// <param name="Resolved">False when no preferred-vendor tier exists (cost defaults to 0).</param>
public record ResolvedBaseUnitCost(
    int PartId,
    decimal CostPerBaseUnit,
    string Currency,
    int? PurchaseUnitId,
    decimal OptionContentQuantity,
    decimal TierUnitPrice,
    int? TierId,
    bool Resolved);
