namespace Forge.Core.Models;

public record BurnRateResponseModel(
    int PartId,
    string PartNumber,
    string Description,
    int? PreferredVendorId,
    string? PreferredVendorName,
    decimal OnHand,
    decimal Available,
    decimal IncomingPoQuantity,
    DateTimeOffset? EarliestPoArrival,
    decimal? BurnRate30Day,          // avg units/day over last 30 days
    decimal? BurnRate60Day,
    decimal? BurnRate90Day,
    decimal? DaysOfStockRemaining,   // based on best available window
    DateTimeOffset? ProjectedStockoutDate,
    decimal? MinStockThreshold,
    decimal? ReorderPoint,
    decimal? ReorderQuantity,
    /// <summary>
    /// Effective lead time as resolved from the preferred VendorPart row, or
    /// null if the part has no preferred VendorPart configured. Vendor-
    /// specific now — Part-level snapshot was retired with the OEM-on-
    /// VendorPart move.
    /// </summary>
    int? LeadTimeDays,
    int? SafetyStockDays,
    bool NeedsReorder);
