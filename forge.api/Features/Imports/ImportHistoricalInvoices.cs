using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Imports;

/// <summary>
/// CSV import of historical invoices, as open or closed AR documents. Rows sharing an invoice number are
/// one invoice; numbers from the file are kept. A paid amount becomes one imported payment applied to the
/// invoice. Nothing posts to the general ledger: with full GL on, the opening AR balance comes from the
/// opening-balance journal and its open items, so posting these would count it twice. No email, overdue
/// notice or integration event fires; an open invoice already past due imports as Overdue.
/// A commit is all-or-nothing.
/// </summary>
public record ImportHistoricalInvoicesCommand(IReadOnlyList<InvoiceImportRowModel> Rows, bool Commit)
    : IRequest<DataImportResultModel>;

public class ImportHistoricalInvoicesHandler(
    AppDbContext db,
    IPaymentRepository payments,
    IBusinessIdentifierService identifiers,
    IClock clock) : IRequestHandler<ImportHistoricalInvoicesCommand, DataImportResultModel>
{
    public const int MaxRows = 5000;
    public const string Provider = "HistoricalImport";
    private const int MaxInvoiceNumberLength = 20;
    private const decimal Tolerance = 0.01m;

    private sealed record LinePlan(int? PartId, string Description, decimal Quantity, decimal UnitPrice);

    private sealed class InvoicePlan(string number, List<(InvoiceImportRowModel Row, int RowNumber)> rows)
    {
        public string Number { get; } = number;
        public List<(InvoiceImportRowModel Row, int RowNumber)> Rows { get; } = rows;
        public List<string> Errors { get; } = [];
        public int? CustomerId { get; set; }
        public DateTimeOffset InvoiceDate { get; set; }
        public DateTimeOffset DueDate { get; set; }
        public List<LinePlan> Lines { get; } = [];
        public decimal TaxRate { get; set; }
        public decimal? TaxLine { get; set; }
        public decimal Paid { get; set; }
        public DateTimeOffset? PaidDate { get; set; }
        public InvoiceStatus Status { get; set; } = InvoiceStatus.Sent;
        public string? CustomerPO { get; set; }
        public int? CreatedId { get; set; }
    }

    public async Task<DataImportResultModel> Handle(ImportHistoricalInvoicesCommand request, CancellationToken ct)
    {
        if (request.Rows.Count > MaxRows)
            throw new InvalidOperationException($"An import can hold at most {MaxRows} rows. Split the file.");

        var orphanRows = new List<(InvoiceImportRowModel Row, int RowNumber)>();
        var plans = new List<InvoicePlan>();
        var byNumber = new Dictionary<string, InvoicePlan>(StringComparer.Ordinal);
        for (var i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var number = row.InvoiceNumber?.Trim();
            if (string.IsNullOrEmpty(number))
            {
                orphanRows.Add((row, i + 1));
                continue;
            }
            if (!byNumber.TryGetValue(number, out var plan))
            {
                plan = new InvoicePlan(number, []);
                byNumber[number] = plan;
                plans.Add(plan);
            }
            plan.Rows.Add((row, i + 1));
        }

        var lookup = await ImportReferenceLookup.LoadAsync(
            db, request.Rows.Select(r => r.Customer), request.Rows.Select(r => r.Part), ct);
        var numbers = byNumber.Keys.ToList();
        var existing = (await db.Invoices.IgnoreQueryFilters()
                .Where(i => numbers.Contains(i.InvoiceNumber))
                .Select(i => i.InvoiceNumber)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var today = clock.UtcNow.UtcDateTime.Date;

        foreach (var plan in plans)
            Validate(plan, lookup, existing, today);

        var invalid = orphanRows.Count + plans.Sum(p => p.Errors.Count > 0 ? p.Rows.Count : 0);
        if (!request.Commit || invalid > 0)
            return Result(plans, orphanRows, committed: false);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var currencies = new DocumentCurrencyResolver(db);
            foreach (var plan in plans)
                await WriteAsync(plan, currencies, ct);
            await tx.CommitAsync(ct);
        }
        return Result(plans, orphanRows, committed: true);
    }

    private static void Validate(InvoicePlan plan, ImportReferenceLookup lookup, HashSet<string> existing, DateTime today)
    {
        var errors = plan.Errors;
        var head = plan.Rows[0].Row;

        if (plan.Number.Length > MaxInvoiceNumberLength)
            errors.Add($"Invoice number '{plan.Number}' is longer than {MaxInvoiceNumberLength} characters.");
        if (existing.Contains(plan.Number))
            errors.Add($"Invoice number '{plan.Number}' is already in use.");

        foreach (var (row, _) in plan.Rows.Skip(1))
        {
            if (Disagrees(row.Customer, head.Customer) || Disagrees(row.InvoiceDate, head.InvoiceDate)
                || Disagrees(row.DueDate, head.DueDate) || Disagrees(row.Tax, head.Tax)
                || Disagrees(row.Total, head.Total) || Disagrees(row.PaidAmount, head.PaidAmount)
                || Disagrees(row.Status, head.Status))
            {
                errors.Add($"Rows for invoice '{plan.Number}' disagree on the customer, dates, tax, total, paid amount or status.");
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(head.Customer))
            errors.Add("Customer is required.");
        else
            plan.CustomerId = lookup.Customer(head.Customer, errors);

        if (head.InvoiceDate is not { } invoiceDate)
            errors.Add("Invoice date is required.");
        else
        {
            plan.InvoiceDate = invoiceDate;
            plan.DueDate = head.DueDate ?? invoiceDate;
            if (plan.DueDate < invoiceDate)
                errors.Add("Due date is before the invoice date.");
        }

        foreach (var (row, rowNumber) in plan.Rows)
        {
            var hasLine = !string.IsNullOrWhiteSpace(row.Description) || !string.IsNullOrWhiteSpace(row.Part)
                || row.UnitPrice.HasValue || row.Quantity.HasValue;
            if (!hasLine) continue;
            var lineErrors = new List<string>();
            var partId = lookup.Part(row.Part, lineErrors);
            var quantity = row.Quantity ?? 1m;
            if (quantity <= 0m) lineErrors.Add($"Row {rowNumber}: quantity must be greater than zero.");
            if (row.UnitPrice is not decimal unitPrice)
            {
                lineErrors.Add($"Row {rowNumber}: a line needs a unit price.");
                unitPrice = 0m;
            }
            else if (unitPrice < 0m) lineErrors.Add($"Row {rowNumber}: unit price can't be negative.");
            errors.AddRange(lineErrors);
            var description = row.Description?.Trim();
            if (string.IsNullOrEmpty(description)) description = row.Part?.Trim() ?? $"Invoice {plan.Number}";
            plan.Lines.Add(new LinePlan(partId, description.Length > 500 ? description[..500] : description, quantity, unitPrice));
        }

        var tax = head.Tax ?? 0m;
        if (tax < 0m) errors.Add("Tax can't be negative.");
        if (plan.Lines.Count == 0)
        {
            if (head.Total is not decimal total)
                errors.Add("An invoice without line columns needs a total.");
            else if (total - tax < 0m)
                errors.Add("Total is less than the tax.");
            else
                plan.Lines.Add(new LinePlan(null, $"Historical invoice {plan.Number}", 1m, total - tax));
        }
        if (errors.Count > 0) return;

        var subtotal = plan.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        if (tax > 0m)
        {
            if (subtotal <= 0m)
                plan.TaxLine = tax;
            else
            {
                var rate = Math.Round(tax / subtotal, 6, MidpointRounding.AwayFromZero);
                if (rate < 100m && Money.Round(subtotal * rate) == tax) plan.TaxRate = rate;
                else plan.TaxLine = tax;
            }
        }
        var invoiceTotal = subtotal + tax;
        if (head.Total is decimal fileTotal && Math.Abs(fileTotal - invoiceTotal) > Tolerance)
            errors.Add($"Total {fileTotal:0.00} does not match the lines plus tax ({invoiceTotal:0.00}).");

        var status = head.Status?.Trim().ToLowerInvariant();
        var paid = head.PaidAmount ?? (status == "paid" ? invoiceTotal : 0m);
        if (paid < 0m) errors.Add("Paid amount can't be negative.");
        if (paid > invoiceTotal + Tolerance) errors.Add("Paid amount is more than the invoice total.");
        plan.Paid = Math.Min(paid, invoiceTotal);
        plan.PaidDate = head.PaidDate;
        plan.CustomerPO = head.CustomerPO?.Trim() is { Length: > 0 } po ? (po.Length > 50 ? po[..50] : po) : null;

        switch (status)
        {
            case null or "" or "open" or "sent" or "overdue" or "partiallypaid" or "partially paid" or "paid":
                if (status == "paid" && plan.Paid < invoiceTotal)
                    errors.Add("Status is Paid but the paid amount is less than the total.");
                plan.Status = plan.Paid >= invoiceTotal && invoiceTotal > 0m ? InvoiceStatus.Paid
                    : plan.Paid > 0m ? InvoiceStatus.PartiallyPaid
                    : plan.DueDate.UtcDateTime.Date < today ? InvoiceStatus.Overdue
                    : InvoiceStatus.Sent;
                if (invoiceTotal == 0m) plan.Status = InvoiceStatus.Paid;
                break;
            case "void" or "voided":
                if (plan.Paid > 0m) errors.Add("A voided invoice can't carry a paid amount.");
                plan.Status = InvoiceStatus.Voided;
                break;
            default:
                errors.Add($"Unknown status '{head.Status!.Trim()}'. Use Open, Paid or Voided (or leave it blank).");
                break;
        }
    }

    private async Task WriteAsync(InvoicePlan plan, DocumentCurrencyResolver currencies, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == plan.CustomerId)
            .Select(c => new { c.CurrencyId, c.PaymentTermsId })
            .FirstAsync(ct);
        var currency = await currencies.ResolveAsync(null, null, customer.CurrencyId, plan.InvoiceDate, ct);

        var invoice = new Invoice
        {
            InvoiceNumber = plan.Number,
            CustomerId = plan.CustomerId!.Value,
            CurrencyId = currency.CurrencyId,
            FxRate = currency.FxRate,
            Status = plan.Status,
            InvoiceDate = plan.InvoiceDate,
            DueDate = plan.DueDate,
            PaymentTermsId = customer.PaymentTermsId,
            TaxRate = plan.TaxRate,
            CustomerPO = plan.CustomerPO,
            Notes = "Historical invoice imported from a file. Not posted to the general ledger.",
            Provider = Provider,
            ExternalRef = $"row {plan.Rows[0].RowNumber}",
        };
        var lineNumber = 1;
        foreach (var line in plan.Lines)
            invoice.Lines.Add(new InvoiceLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
            });
        if (plan.TaxLine is decimal taxLine)
            invoice.Lines.Add(new InvoiceLine
            {
                Description = "Sales tax (imported)",
                Quantity = 1m,
                UnitPrice = taxLine,
                LineNumber = lineNumber,
                TaxCode = InvoiceLine.NonTaxableCode,
            });
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        plan.CreatedId = invoice.Id;
        await identifiers.IssueAsync(BusinessEntityType.Invoice, invoice.Id, invoice.InvoiceNumber, ct);

        if (plan.Paid <= 0m) return;
        var payment = new Payment
        {
            PaymentNumber = await payments.GenerateNextPaymentNumberAsync(ct),
            CustomerId = invoice.CustomerId,
            Method = PaymentMethod.Other,
            Amount = plan.Paid,
            CurrencyId = invoice.CurrencyId,
            FxRate = invoice.FxRate,
            PaymentDate = plan.PaidDate ?? plan.DueDate,
            ReferenceNumber = "Imported",
            Notes = $"Historical payment imported with invoice {invoice.InvoiceNumber}. Not posted to the general ledger.",
            Provider = Provider,
            Applications =
            {
                new PaymentApplication { InvoiceId = invoice.Id, Amount = plan.Paid, SettlementFxRate = invoice.FxRate },
            },
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        await identifiers.IssueAsync(BusinessEntityType.Payment, payment.Id, payment.PaymentNumber, ct);
    }

    private static bool Disagrees<T>(T? value, T? head) =>
        value is not null && (value is not string s || !string.IsNullOrWhiteSpace(s)) && !Equals(Trimmed(value), Trimmed(head));

    private static object? Trimmed<T>(T? value) => value is string s ? s.Trim() : value;

    private static DataImportResultModel Result(
        List<InvoicePlan> plans, List<(InvoiceImportRowModel Row, int RowNumber)> orphans, bool committed)
    {
        var rows = new List<DataImportRowResultModel>();
        foreach (var plan in plans)
            foreach (var (row, rowNumber) in plan.Rows)
                rows.Add(new DataImportRowResultModel(
                    row.RowKey, rowNumber,
                    plan.Errors.Count > 0 ? "Invalid" : committed ? "Created" : "Valid",
                    plan.Errors, plan.Number, plan.CreatedId));
        foreach (var (row, rowNumber) in orphans)
            rows.Add(new DataImportRowResultModel(row.RowKey, rowNumber, "Invalid", ["Invoice number is required."], null, null));
        rows = rows.OrderBy(r => r.RowNumber).ToList();
        var invalid = rows.Count(r => r.Status == "Invalid");
        var invoices = plans.Count(p => p.Errors.Count == 0);
        return new DataImportResultModel(rows.Count, rows.Count - invalid, invalid, committed ? invoices : 0, committed, rows);
    }
}
