namespace Forge.Core.Models;

/// <summary>
/// Per-part landed cost surface. Bought-parts effort PR3.
///
/// <para>The cost tab on a part renders this top-down: a "door-to-door"
/// average over the last N receipts (default 3), an itemized breakdown
/// for each component (base / freight / duty / FX), and a vendor
/// comparison so the buyer can answer "should we switch sources?".</para>
///
/// <para>When no receipt history exists, <see cref="AverageLandedUnitCost"/>
/// is null and <see cref="RecentReceipts"/> is empty. The UI shows a
/// "no receipt history yet" affordance in that case.</para>
/// </summary>
public record PartLandedCostResponseModel(
    int PartId,
    string PartNumber,
    /// <summary>Tenant base currency (e.g. USD). All values below are in this currency.</summary>
    string BaseCurrency,
    /// <summary>Average landed unit cost over the most recent N receipts.</summary>
    decimal? AverageLandedUnitCost,
    /// <summary>Number of receipts contributing to the average.</summary>
    int ReceiptCountUsed,
    /// <summary>Sum of base unit price contributions across the averaged receipts.</summary>
    decimal AverageBaseUnitPrice,
    /// <summary>Sum of allocated-freight contributions, divided into per-unit terms.</summary>
    decimal AverageFreightPerUnit,
    /// <summary>Sum of duty contributions per unit (HTS rate × base × duty applicability).</summary>
    decimal AverageDutyPerUnit,
    /// <summary>FX adjustment (functional vs quote). Zero today; populated when FX layer real.</summary>
    decimal AverageFxAdjustmentPerUnit,
    /// <summary>Per-receipt detail rows feeding the average — most recent first.</summary>
    List<PartLandedCostReceiptModel> RecentReceipts,
    /// <summary>One row per active VendorPart with a most-recent landed cost — sorted ascending so the cheapest source is first.</summary>
    List<VendorLandedCostComparisonModel> VendorComparison);

public record PartLandedCostReceiptModel(
    int ReceivingRecordId,
    string? ReceiptNumber,
    int VendorId,
    string VendorName,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    DateTimeOffset ReceivedAt,
    decimal QuantityReceived,
    decimal BaseUnitPrice,
    decimal AllocatedFreightPerUnit,
    decimal DutyPerUnit,
    decimal FxAdjustmentPerUnit,
    decimal LandedUnitCost);

public record VendorLandedCostComparisonModel(
    int VendorId,
    string VendorName,
    decimal? MostRecentLandedUnitCost,
    DateTimeOffset? MostRecentReceiptAt);
