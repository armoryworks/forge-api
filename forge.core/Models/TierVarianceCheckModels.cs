namespace Forge.Core.Models;

/// <summary>
/// Bought-parts effort PR4 — tier-variance batch check input. Fired by
/// the PO-dialog at save time to identify lines whose entered unit price
/// drifts from the current effective tier on the vendor's price list by
/// more than the configured threshold (`Vendor.OffTierVariancePct` →
/// SystemSetting `purchasing.offTierVariancePct` → 5%).
///
/// <para>Single endpoint covers a whole PO so the user sees one prompt
/// instead of one-per-line. The vendor is fixed across the request
/// (one PO = one vendor); each line carries the part + qty + entered
/// price the user typed.</para>
/// </summary>
public record CheckTierVarianceRequestModel(
    int VendorId,
    List<CheckTierVarianceLineModel> Lines);

public record CheckTierVarianceLineModel(
    int PartId,
    decimal Quantity,
    decimal UnitPrice,
    // The purchase option the entered price/qty are expressed in. Null = priced
    // per base unit ("1 per each"). The variance check matches the tier for the
    // SAME option so the price comparison is apples-to-apples; legacy callers
    // that omit this default to null and match the per-base-unit tiers.
    int? PurchaseUnitId = null);

/// <summary>
/// Response: one row per request line, plus the threshold used so the
/// UI can format its variance display consistently. <c>TierPrice</c> is
/// null when no VendorPart row exists for (vendor, part) — variance is
/// nominally infinite there, but the UI treats "no tier" as "off-tier"
/// and offers an Update Tiers action that creates the first tier.
/// </summary>
public record CheckTierVarianceResponseModel(
    decimal ThresholdPct,
    List<CheckTierVarianceResultModel> Lines);

public record CheckTierVarianceResultModel(
    int PartId,
    decimal Quantity,
    decimal UnitPrice,
    int? VendorPartId,
    decimal? TierPrice,
    string? Currency,
    decimal? VariancePct,
    bool IsOffTier);
