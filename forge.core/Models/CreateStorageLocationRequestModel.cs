using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CreateStorageLocationRequestModel(
    string Name,
    LocationType LocationType,
    int? ParentId,
    string? Barcode,
    string? Description);
