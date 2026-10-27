namespace Forge.Core.Models;

public record ScanIssueSourceModel(
    int BinContentId,
    int LocationId,
    string LocationPath,
    string? LocationBarcode,
    string? LotNumber,
    DateOnly? ExpirationDate,
    decimal AvailableQuantity);
