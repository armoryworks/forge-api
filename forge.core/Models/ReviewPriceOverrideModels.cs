namespace Forge.Core.Models;

/// <summary>
/// AI-assisted price-variance review for a manual PO unit-price override (forge#6).
/// </summary>
public record ReviewPriceOverrideRequestModel(
    int VendorId,
    int PartId,
    decimal Quantity,
    int? PurchaseUnitId,
    decimal EnteredUnitPrice,
    string? Reason);

public record ReviewPriceOverrideResponseModel(
    decimal? TierPrice,
    decimal? VariancePct,
    bool IsOffTier,
    string RiskLevel,
    string Assessment,
    string SuggestedJustification,
    bool AiAvailable);
