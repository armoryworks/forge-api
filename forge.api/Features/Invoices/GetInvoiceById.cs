using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceByIdQuery(int Id) : IRequest<InvoiceDetailResponseModel>;

public class GetInvoiceByIdHandler(IInvoiceRepository repo)
    : IRequestHandler<GetInvoiceByIdQuery, InvoiceDetailResponseModel>
{
    public async Task<InvoiceDetailResponseModel> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var subtotal = invoice.Lines.Sum(l => l.Quantity * l.UnitPrice);
        var taxAmount = subtotal * invoice.TaxRate;
        var total = subtotal + taxAmount;
        var amountPaid = invoice.PaymentApplications.Sum(pa => pa.Amount);

        return new InvoiceDetailResponseModel(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CustomerId,
            invoice.Customer.Name,
            invoice.SalesOrderId,
            invoice.SalesOrder?.OrderNumber,
            invoice.ShipmentId,
            invoice.Shipment?.ShipmentNumber,
            invoice.Status.ToString(),
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.CreditTerms?.ToString(),
            invoice.TaxRate,
            subtotal,
            taxAmount,
            total,
            amountPaid,
            total - amountPaid,
            invoice.CustomerPO,
            invoice.Notes,
            invoice.Lines.Select(l => new InvoiceLineResponseModel(
                l.Id, l.PartId, l.Part?.PartNumber, l.Description,
                l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice, l.LineNumber)).ToList(),
            invoice.PaymentApplications.Select(pa => new PaymentApplicationResponseModel(
                pa.Id, pa.PaymentId, pa.Payment.PaymentNumber,
                pa.InvoiceId, invoice.InvoiceNumber, pa.Amount)).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt);
    }
}
