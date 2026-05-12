namespace Forge.Core.Models;

public record PoCalendarResponseModel(
    int Id,
    string PoNumber,
    string VendorName,
    DateOnly ExpectedDeliveryDate,
    string Status,
    int LineCount
);
