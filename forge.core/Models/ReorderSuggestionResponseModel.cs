using Forge.Core.Enums;

namespace Forge.Core.Models;

public record ReorderSuggestionResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string PartDescription,
    int? VendorId,
    string? VendorName,
    decimal CurrentStock,
    decimal AvailableStock,
    decimal BurnRateDailyAvg,
    int BurnRateWindowDays,
    int? DaysOfStockRemaining,
    DateTimeOffset? ProjectedStockoutDate,
    decimal IncomingPoQuantity,
    DateTimeOffset? EarliestPoArrival,
    decimal SuggestedQuantity,
    ReorderSuggestionStatus Status,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    int? ResultingPurchaseOrderId,
    string? DismissReason,
    string? DismissedByName,
    DateTimeOffset? DismissedAt,
    string? Notes,
    DateTimeOffset CreatedAt);
