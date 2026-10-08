namespace Forge.Core.Models;

public record PartQualityLotModel(
    int Id,
    string LotNumber,
    decimal OnHandQuantity,
    decimal HeldQuantity,
    bool IsOnHold,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset CreatedAt);
