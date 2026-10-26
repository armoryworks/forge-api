using MediatR;
using Forge.Api.Features.PaymentTerms;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Invoices;

public record GetInvoiceByIdQuery(int Id) : IRequest<InvoiceDetailResponseModel>;

public class GetInvoiceByIdHandler(IInvoiceRepository repo, AppDbContext? db = null)
    : IRequestHandler<GetInvoiceByIdQuery, InvoiceDetailResponseModel>
{
    public async Task<InvoiceDetailResponseModel> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var subtotal = invoice.Subtotal;
        var taxAmount = invoice.TaxAmount;
        var total = invoice.Total;
        var amountPaid = invoice.AmountPaid;
        var discounts = db is null ? DiscountSettings.Default : await DiscountSettings.ReadAsync(db, cancellationToken);

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
            invoice.BalanceDue,
            invoice.CustomerPO,
            invoice.Notes,
            invoice.Lines.Select(l => new InvoiceLineResponseModel(
                l.Id, l.PartId, l.Part?.PartNumber, l.Description,
                l.Quantity, l.UnitPrice, l.LineTotal, l.LineNumber, l.IsTaxable, l.LineType)).ToList(),
            invoice.PaymentApplications.Select(pa => new PaymentApplicationResponseModel(
                pa.Id, pa.PaymentId, pa.Payment.PaymentNumber,
                pa.InvoiceId, invoice.InvoiceNumber, pa.Amount, pa.DiscountAmount)).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.Version,
            invoice.Currency?.Code,
            invoice.CurrencyId,
            invoice.FxRate,
            invoice.FreightSubtotal,
            invoice.SalesTaxRateId,
            invoice.PaymentTermsId,
            invoice.DiscountPercent,
            invoice.DiscountDueDate,
            EarlyPaymentDiscount.Deadline(invoice.DiscountDueDate, discounts.GraceDays),
            invoice.DiscountsTaken,
            invoice.BalanceDue > 0m ? EarlyPaymentDiscountRules.RemainingMaximum(invoice, discounts) : 0m,
            db is null ? null : await new PaymentTermsResolver(db).ModelAsync(invoice.PaymentTermsId, invoice.CreditTerms, cancellationToken),
            invoice.CreditsApplied,
            invoice.CreditApplications
                .Where(a => a.ReversedAt == null)
                .OrderBy(a => a.AppliedAt)
                .Select(a => new CreditMemoApplicationModel(
                    a.Id, a.CreditMemoId, a.CreditMemo?.CreditMemoNumber, a.InvoiceId, invoice.InvoiceNumber,
                    a.Amount, a.AppliedAt, a.ReversedAt))
                .ToList());
    }
}
