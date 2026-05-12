namespace Forge.Core.Models;

public record RecordVendorResponseRequestModel(
    int VendorId,
    decimal? UnitPrice,
    int? LeadTimeDays,
    decimal? MinimumOrderQuantity,
    decimal? ToolingCost,
    DateTimeOffset? QuoteValidUntil,
    string? Notes);
