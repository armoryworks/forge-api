namespace Forge.Core.Models;

/// <summary>
/// Compact dashboard rollup the portal landing page renders as KPI cards.
/// Each count is the number of rows the calling contact's customer can see
/// for that category, narrowed to the "interesting" subset (open / pending
/// rather than the full archive). Standalone-mode-only fields (Invoices)
/// are returned as zero when the install has an external accounting
/// provider connected.
/// </summary>
public record PortalSummaryResponseModel(
    int OpenSalesOrderCount,
    int OpenQuoteCount,
    int OpenInvoiceCount,
    int InTransitShipmentCount);

public record PortalSalesOrderListItem(
    int Id,
    string OrderNumber,
    string Status,
    DateTimeOffset OrderDate,
    DateTimeOffset? RequestedDate,
    decimal Total);

public record PortalQuoteListItem(
    int Id,
    string QuoteNumber,
    string QuoteType,
    string Status,
    DateTimeOffset QuoteDate,
    DateTimeOffset? ExpiresAt,
    decimal Total);

public record PortalInvoiceListItem(
    int Id,
    string InvoiceNumber,
    string Status,
    DateTimeOffset InvoiceDate,
    DateTimeOffset? DueDate,
    decimal Total,
    decimal AmountPaid,
    decimal Balance);

public record PortalShipmentListItem(
    int Id,
    string ShipmentNumber,
    string Status,
    DateTimeOffset? ShippedDate,
    DateTimeOffset? DeliveredDate,
    string? Carrier,
    string? TrackingNumber);
