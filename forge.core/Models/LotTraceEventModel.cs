namespace Forge.Core.Models;

/// <summary>
/// One entry in a lot's flattened, date-ordered trace timeline — the shape the
/// lot detail panel renders. Type is one of Job / ProductionRun / PurchaseOrder /
/// BinLocation / QcInspection (drives the timeline icon).
/// </summary>
public record LotTraceEventModel(
    string Type,
    string ReferenceNumber,
    string Description,
    DateTimeOffset Date,
    decimal? Quantity);
