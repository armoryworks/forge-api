namespace Forge.Core.Models;

/// <summary>
/// S4a — live view of the internal customer-PO document. Everything except
/// the identity fields (DocumentNumber / GeneratedAt / GeneratedFromQuoteId)
/// is read from the CURRENT state of the linked sales order at request time;
/// the document is a dynamic view, not a snapshot.
/// </summary>
public record CustomerPoDocumentResponseModel(
    int Id,
    string DocumentNumber,
    DateTimeOffset GeneratedAt,
    int? GeneratedFromQuoteId,
    string? QuoteNumber,
    int SalesOrderId,
    string OrderNumber,
    string Status,
    string? CustomerPO,
    int CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? ShippingAddress,
    string? BillingAddress,
    List<CustomerPoDocumentLineModel> Lines,
    decimal Subtotal,
    decimal TaxRate,
    decimal TaxAmount,
    decimal Total);
