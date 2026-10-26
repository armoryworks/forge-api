using System.Net;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Features.Accounting;
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
/// company name is not set, or when nothing is owed any more. A bucket reached more than
/// <see cref="CatchUpDays"/> days ago is not reminded, so turning the capability on, or upgrading an install that
/// already holds aged invoices, does not mail every customer at once. Conversion opening-balance documents are
/// never reminded: they stand for invoices the customer received from the previous system.
/// </summary>
public static class InvoiceDunning
{
    public const string Capability = "CAP-O2C-COLLECTIONS";
    public const string BucketDue = "due";

    /// <summary>How many days after an invoice reaches a bucket its reminder may still go out.</summary>
    public const int CatchUpDays = 7;

    public static string BucketFor(int daysOverdue) => daysOverdue switch
    {
        >= 90 => "90",
        >= 60 => "60",
        >= 30 => "30",
        _ => BucketDue,
    };

    public static string OperationKey(int invoiceId, string bucket) => $"invoice-dunning:{invoiceId}:{bucket}";

    /// <summary>The days overdue at which <paramref name="bucket"/> is reached.</summary>
    public static int BucketStartDays(string bucket) => bucket == BucketDue ? 0 : int.Parse(bucket, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Queues the reminder; returns false when it was skipped or had already been queued.</summary>
    public static async Task<bool> EnqueueReminderAsync(
        AppDbContext db,
        IIntegrationOutboxService outbox,
        int invoiceId,
        string bucket,
        DateTimeOffset now,
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

        if (invoice.Provider == ConversionOpenItems.Provider)
        {
            Log.Information("Payment reminder skipped: invoice {InvoiceNumber} is a conversion opening balance.", invoice.InvoiceNumber);
            return false;
        }

        var daysOverdue = (int)Math.Floor((now - invoice.DueDate).TotalDays);
        if (daysOverdue - BucketStartDays(bucket) > CatchUpDays)
        {
            Log.Information(
                "Payment reminder skipped: invoice {InvoiceNumber} reached the {Bucket} bucket {Days} days ago, past the {CatchUp}-day catch-up.",
                invoice.InvoiceNumber, bucket, daysOverdue - BucketStartDays(bucket), CatchUpDays);
            return false;
        }

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
        var money = await MoneyFormat.ForCurrencyIdAsync(db, invoice.CurrencyId, ct);
        var balance = money.Format(invoice.BalanceDue);
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
                    new InvoicePdfDocument(invoice, companyName, money, await InvoiceDiscountOffer.ForAsync(db, invoice, now, ct)).GeneratePdf()),
            ]);

        await outbox.EnqueueEmailAsync(operationKey, message, entityType: "Invoice", entityId: invoice.Id, ct: ct);
        return true;
    }
}
