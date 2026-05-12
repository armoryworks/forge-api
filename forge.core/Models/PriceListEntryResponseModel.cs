namespace Forge.Core.Models;

public record PriceListEntryResponseModel(
    int Id,
    int PriceListId,
    int PartId,
    string PartNumber,
    string PartName,
    decimal UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
