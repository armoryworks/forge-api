using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>S2a: payload for editing an existing storage location (rename / re-type / re-parent).</summary>
public record UpdateStorageLocationRequestModel(
    string Name,
    LocationType LocationType,
    int? ParentId,
    string? Barcode,
    string? Description);
