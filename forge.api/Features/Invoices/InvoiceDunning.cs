using System.Net;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// CAP-O2C-COLLECTIONS — the payment reminder an overdue invoice sends its customer. One reminder per invoice
/// and bucket (<c>due</c>, <c>30</c>, <c>60</c>, <c>90</c>): the outbox operation key
/// <c>invoice-dunning:{id}:{bucket}</c> makes a daily re-check or a retry a no-op. Reminders go to the
/// <see cref="InvoiceRecipientResolver"/> recipient and are skipped (logged) when there is none, when the
/// company name is not set, or when nothing is owed any more.
/// </summary>
public static class InvoiceDunning
{
    public const string Capability = "CAP-O2C-COLLECTIONS";
    public const string BucketDue = "due";

    public static string BucketFor(int daysOverdue) => daysOverdue switch
    {
        >= 90 => "90",
        >= 60 => "60",
        >= 30 => "30",
        _ => BucketDue,
    };

    public static string OperationKey(int invoiceId, string bucket) => $"invoice-dunning:{invoiceId}:{bucket}";

    /// <summary>Queues the reminder; returns false when it was skipped or had already been queued.</summary>
    public static async Task<bool> EnqueueReminderAsync(
        AppDbContext db,
        IIntegrationOutboxService outbox,
        int invoiceId,
        string bucket,
        CancellationToken ct)
    {
        var operationKey = OperationKey(invoiceId, bucket);
        if (await db.IntegrationOutboxEntries.AsNoTracking().AnyAsync(e => e.OperationKey == operationKey, ct))
            return false;

        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines).ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null
            || invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Paid or InvoiceStatus.Voided
            || invoice.BalanceDue <= 0m)
            return false;

        var recipient = (await InvoiceRecipientResolver.ResolveAsync(db, invoice.CustomerId, ct)).Email;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            Log.Warning("Payment reminder skipped: invoice {InvoiceNumber} customer has no email on file.", invoice.InvoiceNumber);
            return false;
        }

        var companyName = await CompanyIdentity.GetCompanyNameAsync(db, ct);
        if (companyName is null)
        {
            Log.Warning("Payment reminder skipped: company name is not set (invoice {InvoiceNumber}).", invoice.InvoiceNumber);
            return false;
        }

        var customerName = WebUtility.HtmlEncode(invoice.Customer.CompanyName ?? invoice.Customer.Name);
        var company = WebUtility.HtmlEncode(companyName);
        var balance = InvoiceMoneyText.Format(invoice.BalanceDue, await InvoiceMoneyText.CurrencyAsync(db, invoice.CurrencyId, ct));
        var lateness = bucket == BucketDue ? "is now past due" : $"is more than {bucket} days past due";

        var message = new EmailMessage(
            To: recipient,
            Subject: $"Payment reminder: invoice {invoice.InvoiceNumber} from {companyName}",
            HtmlBody: $"""
                <h2>Payment reminder</h2>
                <p>Dear {customerName},</p>
                <p>Invoice {WebUtility.HtmlEncode(invoice.InvoiceNumber)}, due <strong>{invoice.DueDate:MM/dd/yyyy}</strong>, {lateness}.
                The balance due is <strong>{balance}</strong>.</p>
                <p>If you have already paid, please disregard this reminder. The invoice is attached.</p>
                <p>— {company}</p>
                """,
            Attachments:
            [
                new EmailAttachment(
                    $"Invoice-{invoice.InvoiceNumber}.pdf",
                    "application/pdf",
                    new InvoicePdfDocument(invoice, companyName).GeneratePdf()),
            ]);

        await outbox.EnqueueEmailAsync(operationKey, message, entityType: "Invoice", entityId: invoice.Id, ct: ct);
        return true;
    }
}
