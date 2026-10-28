using Forge.Core.Enums;

namespace Forge.Core.Models;

// Phase 3 / WU-10 / F8-partial — quantities are decimal (was int).
public record SalesOrderLineResponseModel(
    int Id,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    int LineNumber,
    decimal ShippedQuantity,
    decimal RemainingQuantity,
    bool IsFullyShipped,
    string? Notes,
    List<SalesOrderLineJobModel> Jobs,
    bool NeedsJob,
    bool ShipsGoods = true,
    DocumentLineType LineType = DocumentLineType.Item,
    bool Taxable = true,
    List<SalesOrderLinePurchaseModel>? Purchases = null,
    decimal? LineOverShipTolerancePct = null,
    decimal? LineUnderShipTolerancePct = null,
    decimal OverShipTolerancePct = 0m,
    decimal UnderShipTolerancePct = 0m,
    decimal MaxShippableQuantity = 0m,
    decimal MinCompleteQuantity = 0m,
    decimal ShortClosedQuantity = 0m,
    bool ClosedWithinTolerance = false);
