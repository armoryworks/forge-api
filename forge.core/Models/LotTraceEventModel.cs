namespace Forge.Core.Models;

/// <summary>
/// One entry in a lot's flattened, date-ordered trace timeline — the shape the
/// lot detail panel renders. Type is one of Job / ProductionRun / PurchaseOrder /
/// BinLocation / QcInspection / ConsumedInput / ConsumedInto (drives the timeline icon).
/// StatusCode is the untranslated status of a ProductionRun or QcInspection event and Actor
/// the inspector; Description stays a composed English fallback for older clients.
/// </summary>
public record LotTraceEventModel(
    string Type,
    string ReferenceNumber,
    string Description,
    DateTimeOffset Date,
    decimal? Quantity,
    string? StatusCode = null,
    string? Actor = null);
