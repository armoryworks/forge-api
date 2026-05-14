
namespace Forge.Core.Models;

public record StorageLocationFlatResponseModel(
    int Id,
    string Name,
    LocationType LocationType,
    string? Barcode,
    string LocationPath);
