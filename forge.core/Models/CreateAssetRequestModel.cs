using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Asset-create payload. Phase 3 F4 extends with acquisitionCost,
/// depreciationMethod, workCenterId, glAccount so the small-shop onboarding
/// form does not need a PATCH-after-create round trip. The first 12 positional
/// fields preserve the original signature for compile parity.
/// </summary>
public record CreateAssetRequestModel(
    string Name,
    AssetType AssetType,
    string? Location,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? Notes,
    bool? IsCustomerOwned,
    int? CavityCount,
    int? ToolLifeExpectancy,
    int? SourceJobId,
    int? SourcePartId,
    // F4 — full-record fields. All nullable / defaulted so existing callers
    // that pass only the 12 originals continue to compile and behave the same.
    decimal? AcquisitionCost = null,
    DepreciationMethod? DepreciationMethod = null,
    int? WorkCenterId = null,
    string? GlAccount = null);
