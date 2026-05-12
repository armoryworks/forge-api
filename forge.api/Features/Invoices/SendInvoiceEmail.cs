using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record SendInvoiceEmailCommand(int Id, string RecipientEmail) : IRequest;

public class SendInvoiceEmailHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IIntegrationOutboxService outbox) : IRequestHandler<SendInvoiceEmailCommand>
{
    public async Task Handle(SendInvoiceEmailCommand request, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var pdfDoc = new InvoicePdfDocument(invoice, companyName);
        var pdfBytes = pdfDoc.GeneratePdf();

        var customerName = invoice.Customer.CompanyName ?? invoice.Customer.Name;
        var message = new EmailMessage(
            To: request.RecipientEmail,
            Subject: $"Invoice {invoice.InvoiceNumber} from {companyName}",
            HtmlBody: $"""
                <h2>Invoice {invoice.InvoiceNumber}</h2>
                <p>Dear {customerName},</p>
                <p>Please find your invoice attached. The total amount is <strong>{invoice.Total:C}</strong>,
                due by <strong>{invoice.DueDate:MMM dd, yyyy}</strong>.</p>
                <p>Thank you for your business.</p>
                <p>— {companyName}</p>
                """,
            Attachments:
            [
                new EmailAttachment(
                    $"Invoice-{invoice.InvoiceNumber}.pdf",
                    "application/pdf",
                    pdfBytes)
            ]);

        var operationKey = $"invoice-email:{invoice.Id}:{request.RecipientEmail}";
        await outbox.EnqueueEmailAsync(
            operationKey,
            message,
            entityType: "Invoice",
            entityId: invoice.Id,
            ct: ct);
    }
}
