namespace Forge.Core.Models;

/// <summary>
/// #27 — a sales-order line offered for inline association when creating a job.
/// <see cref="AssignedJobCount"/> is the number of open (non-archived, non-disposed)
/// jobs already linked to the line; the picker hides lines with a positive count by
/// default and surfaces them only when the "show already-assigned" override is on.
/// </summary>
public record AssignableSalesOrderLineModel(
    int Id,
    int SalesOrderId,
    string OrderNumber,
    int LineNumber,
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    int AssignedJobCount);
