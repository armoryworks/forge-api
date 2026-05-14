
namespace Forge.Core.Models;

public record UpdateAssetRequestModel(
    string? Name,
    AssetType? AssetType,
    string? Location,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    AssetStatus? Status,
    decimal? CurrentHours,
    string? Notes,
    bool? IsCustomerOwned,
    int? CavityCount,
    int? ToolLifeExpectancy,
    int? CurrentShotCount,
    int? SourceJobId,
    int? SourcePartId);
