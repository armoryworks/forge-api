
namespace Forge.Core.Models;

public record StorageLocationResponseModel(
    int Id,
    string Name,
    LocationType LocationType,
    int? ParentId,
    string? Barcode,
    string? Description,
    int SortOrder,
    bool IsActive,
    string LocationPath,
    int ContentCount,
    List<StorageLocationResponseModel> Children);
