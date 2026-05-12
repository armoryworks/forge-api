using Forge.Core.Enums;

namespace Forge.Core.Models;

public record BinStockResponseModel(
    int LocationId,
    string LocationName,
    string LocationPath,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    BinContentStatus Status,
    string? LotNumber,
    int? LotId,
    DateTimeOffset? LotExpirationDate,
    string? SupplierLotNumber);
