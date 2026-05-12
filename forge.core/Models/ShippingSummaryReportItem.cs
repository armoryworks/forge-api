namespace Forge.Core.Models;

public record ShippingSummaryReportItem(
    string Status,
    int Count,
    // Phase 3 / WU-23 (F8-broad): aggregate of ShipmentLine.Quantity (now decimal).
    decimal TotalItems,
    int OnTimeCount,
    int LateCount);
