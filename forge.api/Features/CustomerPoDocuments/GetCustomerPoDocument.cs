using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPoDocuments;

public record GetCustomerPoDocumentQuery(int SalesOrderId) : IRequest<CustomerPoDocumentResponseModel>;

/// <summary>
/// S4a — live view of the internal customer-PO document. Only the identity
/// fields come from the CustomerPoDocument row; the header, customer block,
/// lines, and totals are read from the CURRENT sales order every time, so
/// the document always reflects downstream SO edits (dynamic view, not a
/// snapshot).
/// </summary>
public class GetCustomerPoDocumentHandler(AppDbContext db)
    : IRequestHandler<GetCustomerPoDocumentQuery, CustomerPoDocumentResponseModel>
{
    public async Task<CustomerPoDocumentResponseModel> Handle(
        GetCustomerPoDocumentQuery request, CancellationToken ct)
    {
        var document = await db.CustomerPoDocuments
            .AsNoTracking()
            .Include(d => d.GeneratedFromQuote)
            .Include(d => d.SalesOrder).ThenInclude(so => so.Customer)
            .Include(d => d.SalesOrder).ThenInclude(so => so.Lines).ThenInclude(l => l.Part)
            .Include(d => d.SalesOrder).ThenInclude(so => so.ShippingAddress)
            .Include(d => d.SalesOrder).ThenInclude(so => so.BillingAddress)
            .FirstOrDefaultAsync(d => d.SalesOrderId == request.SalesOrderId, ct)
            ?? throw new KeyNotFoundException(
                $"No customer PO document exists for sales order {request.SalesOrderId}");

        var order = document.SalesOrder;

        var lines = order.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new CustomerPoDocumentLineModel(
                l.LineNumber, l.Description, l.Part?.PartNumber,
                l.Quantity, l.UnitPrice, l.LineTotal))
            .ToList();

        return new CustomerPoDocumentResponseModel(
            document.Id,
            document.DocumentNumber,
            document.GeneratedAt,
            document.GeneratedFromQuoteId,
            document.GeneratedFromQuote?.QuoteNumber,
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.CustomerPO,
            order.CustomerId,
            order.Customer.GetDisplayName(),
            order.Customer.Email,
            order.Customer.Phone,
            FormatAddress(order.ShippingAddress),
            FormatAddress(order.BillingAddress),
            lines,
            order.Subtotal,
            order.TaxRate,
            order.TaxAmount,
            order.Total);
    }

    private static string? FormatAddress(CustomerAddress? address)
    {
        if (address is null)
            return null;

        var parts = new List<string> { address.Line1 };
        if (!string.IsNullOrWhiteSpace(address.Line2))
            parts.Add(address.Line2);
        parts.Add($"{address.City}, {address.State} {address.PostalCode}");
        if (!string.IsNullOrWhiteSpace(address.Country) && address.Country != "US")
            parts.Add(address.Country);

        return string.Join(", ", parts);
    }
}
