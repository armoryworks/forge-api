
namespace Forge.Core.Models;

public record AssetResponseModel(
    int Id,
    string Name,
    AssetType AssetType,
    string? Location,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    AssetStatus Status,
    string? PhotoFileId,
    decimal CurrentHours,
    string? Notes,
    bool IsCustomerOwned,
    int? CavityCount,
    int? ToolLifeExpectancy,
    int CurrentShotCount,
    int? SourceJobId,
    string? SourceJobNumber,
    int? SourcePartId,
    string? SourcePartNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Phase 3 F4 — surface full-record fields on the GET so a POST-with-
    // everything can be verified via a single round-trip.
    decimal? AcquisitionCost = null,
    DepreciationMethod? DepreciationMethod = null,
    int? WorkCenterId = null,
    string? WorkCenterCode = null,
    string? GlAccount = null);
