namespace Forge.Core.Models;

public record RfqVendorResponseModel(
    int Id,
    int RfqId,
    int VendorId,
    string VendorName,
    string ResponseStatus,
    decimal? UnitPrice,
    int? LeadTimeDays,
    decimal? MinimumOrderQuantity,
    decimal? ToolingCost,
    DateTimeOffset? QuoteValidUntil,
    string? Notes,
    DateTimeOffset? InvitedAt,
    DateTimeOffset? RespondedAt,
    bool IsAwarded,
    string? DeclineReason);
