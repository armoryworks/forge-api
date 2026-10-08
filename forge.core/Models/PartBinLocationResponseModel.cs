using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PartBinLocationResponseModel(
    int BinContentId,
    string LocationPath,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    string? LotNumber,
    BinContentStatus Status);
