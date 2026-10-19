using FluentValidation;
using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Invoices;

public record SendInvoiceEmailCommand(int Id, string? RecipientEmail) : IRequest;

public class SendInvoiceEmailValidator : AbstractValidator<SendInvoiceEmailCommand>
{
    public SendInvoiceEmailValidator()
    {
        RuleFor(x => x.RecipientEmail!.Trim())
            .EmailAddress()
            .MaximumLength(320)
            .When(x => !string.IsNullOrWhiteSpace(x.RecipientEmail))
            .OverridePropertyName("recipientEmail");
    }
}

/// <summary>
/// Emails the invoice PDF to the customer. A blank recipient falls back to
/// <see cref="InvoiceRecipientResolver"/> (the Billing contact first). Only a finalized, non-void invoice
/// can be emailed. The same invoice to the same address queues once (outbox idempotency).
/// </summary>
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

        if (invoice.Status == InvoiceStatus.Draft)
            throw new InvalidOperationException("Finalize the invoice before emailing it to the customer.");
        if (invoice.Status == InvoiceStatus.Voided)
            throw new InvalidOperationException("A voided invoice cannot be emailed.");

        var recipient = string.IsNullOrWhiteSpace(request.RecipientEmail)
            ? (await InvoiceRecipientResolver.ResolveAsync(db, invoice.CustomerId, ct)).Email
            : request.RecipientEmail.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
            throw new InvalidOperationException(
                "The customer has no email address on file. Add a Billing contact or enter a recipient.");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct)
            ?? throw new InvalidOperationException(
                "Set your company name in Admin > Company before sending documents to customers.");

        var pdfDoc = new InvoicePdfDocument(invoice, companyName);
        var pdfBytes = pdfDoc.GeneratePdf();

        var customerName = invoice.Customer.CompanyName ?? invoice.Customer.Name;
        var total = InvoiceMoneyText.Format(invoice.Total, await InvoiceMoneyText.CurrencyAsync(db, invoice.CurrencyId, ct));
        var message = new EmailMessage(
            To: recipient,
            Subject: $"Invoice {invoice.InvoiceNumber} from {companyName}",
            HtmlBody: $"""
                <h2>Invoice {invoice.InvoiceNumber}</h2>
                <p>Dear {customerName},</p>
                <p>Please find your invoice attached. The total amount is <strong>{total}</strong>,
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

        var operationKey = $"invoice-email:{invoice.Id}:{recipient}";
        await outbox.EnqueueEmailAsync(
            operationKey,
            message,
            entityType: "Invoice",
            entityId: invoice.Id,
            ct: ct);

        db.LogActivityAt("emailed", $"Emailed invoice {invoice.InvoiceNumber} to {recipient}", ("Invoice", invoice.Id));
        await db.SaveChangesAsync(ct);
    }
}
