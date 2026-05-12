namespace Forge.Core.Models;

public record AutoPoSuggestionResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string? PartDescription,
    int VendorId,
    string VendorName,
    decimal SuggestedQty,
    DateTimeOffset NeededByDate,
    string Status,
    List<string>? SourceSalesOrderNumbers,
    DateTimeOffset CreatedAt);
