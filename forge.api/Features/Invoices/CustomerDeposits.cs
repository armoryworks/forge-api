using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — customer deposits billed in advance on payment-milestone invoices. A milestone invoice
/// is an advance billing: it posts Dr AR / Cr CUSTOMER_DEPOSITS, not revenue. Every later invoice for the same
/// sales order carries a <see cref="DocumentLineType.Deposit"/> line (negative, non-taxable, after tax) that uses up
/// what is left of the order's billed deposits, so the customer is not billed twice and the deposit liability is
/// relieved as the goods are invoiced. A deposit counts once its invoice is sent, less any credit memo issued against
/// it, and stops counting when it is voided; an invoice uses deposits once it is sent and gives them back when it is
/// voided.
/// </summary>
public static class CustomerDeposits
{
    /// <summary>A caller supplied a deposit line; the system adds them.</summary>
    public const string ManualLineCode = "deposit-line-manual";

    /// <summary>A deposit invoice whose deposit later invoices have already used is being voided.</summary>
    public const string DepositInUseCode = "deposit-invoice-in-use";

    private static readonly InvoiceStatus[] Uncounted = [InvoiceStatus.Draft, InvoiceStatus.Voided];

    /// <summary>True when the invoice bills a payment milestone, i.e. is a deposit (advance) invoice.</summary>
    public static Task<bool> IsDepositInvoiceAsync(AppDbContext db, int invoiceId, CancellationToken ct)
        => db.PaymentMilestones.AnyAsync(m => m.InvoiceId == invoiceId, ct);

    /// <summary>
    /// Replaces the deposit line on a Draft invoice with one for what is left of its sales order's deposits, capped
    /// at the invoice's own total. Does nothing for an invoice that is not Draft, has no sales order, or is itself a
    /// deposit invoice. Adds to the change tracker only; the caller saves.
    /// </summary>
    public static async Task ApplyAsync(AppDbContext db, Invoice invoice, CancellationToken ct)
    {
        if (invoice.Status != InvoiceStatus.Draft || invoice.SalesOrderId is not int salesOrderId)
            return;
        if (invoice.Id != 0 && await IsDepositInvoiceAsync(db, invoice.Id, ct))
            return;

        foreach (var stale in invoice.Lines.Where(l => l.LineType == DocumentLineType.Deposit).ToList())
        {
            invoice.Lines.Remove(stale);
            if (stale.Id != 0)
                db.Remove(stale);
        }

        var pool = await PoolAsync(db, salesOrderId, invoice.CurrencyId, invoice.Id, ct);
        var gross = Money.Round(invoice.Total);
        var amount = Math.Min(pool.Remaining, gross);
        if (amount <= 0m)
            return;

        invoice.Lines.Add(new InvoiceLine
        {
            PartId = null,
            Description = $"Less deposit billed on {string.Join(", ", pool.DepositNumbers)}",
            Quantity = 1m,
            UnitPrice = -amount,
            LineNumber = invoice.Lines.Count == 0 ? 1 : invoice.Lines.Max(l => l.LineNumber) + 1,
            LineType = DocumentLineType.Deposit,
            TaxCode = InvoiceLine.NonTaxableCode,
        });
    }

    /// <summary>
    /// Refuses to void a deposit invoice when the sales order's other sent invoices have used more deposit than
    /// would be left without it.
    /// </summary>
    public static async Task EnsureVoidableAsync(AppDbContext db, Invoice invoice, CancellationToken ct)
    {
        if (invoice.SalesOrderId is not int salesOrderId || !await IsDepositInvoiceAsync(db, invoice.Id, ct))
            return;

        var pool = await PoolAsync(db, salesOrderId, invoice.CurrencyId, invoice.Id, ct);
        if (pool.Used > pool.Billed)
            throw Failure("Id", DepositInUseCode,
                $"Invoice {invoice.InvoiceNumber} bills a deposit that later invoices for this order have already used. Void those invoices first.");
    }

    /// <summary>A 400 carrying <paramref name="code"/> so the UI can show its own translated message.</summary>
    public static ValidationException Failure(string property, string code, string message)
        => new(message, [new ValidationFailure(property, message) { ErrorCode = code }]);

    /// <summary>
    /// The deposit billed for a sales order (sent, unvoided milestone invoices in <paramref name="currencyId"/>),
    /// the functional value it was booked at, and what the order's other sent, unvoided invoices have used.
    /// </summary>
    public static async Task<DepositPool> PoolAsync(
        AppDbContext db, int salesOrderId, int currencyId, int excludeInvoiceId, CancellationToken ct)
    {
        var depositInvoiceIds = await db.PaymentMilestones.AsNoTracking()
            .Where(m => m.InvoiceId != null && m.PaymentSchedule.SalesOrderId == salesOrderId)
            .Select(m => m.InvoiceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var deposits = depositInvoiceIds.Count == 0
            ? []
            : await db.Invoices.AsNoTracking()
                .Include(i => i.Lines)
                .Where(i => depositInvoiceIds.Contains(i.Id) && i.Id != excludeInvoiceId
                    && i.CurrencyId == currencyId && !Uncounted.Contains(i.Status))
                .OrderBy(i => i.Id)
                .ToListAsync(ct);

        var used = await db.InvoiceLines.AsNoTracking()
            .Where(l => l.LineType == DocumentLineType.Deposit
                && l.Invoice.SalesOrderId == salesOrderId
                && l.InvoiceId != excludeInvoiceId
                && l.Invoice.CurrencyId == currencyId
                && !Uncounted.Contains(l.Invoice.Status))
            .SumAsync(l => -(l.Quantity * l.UnitPrice), ct);

        var depositIds = deposits.Select(i => i.Id).ToList();
        var credited = depositIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await db.CreditMemos.AsNoTracking()
                .Include(m => m.Lines)
                .Where(m => m.InvoiceId != null && depositIds.Contains(m.InvoiceId.Value)
                    && m.Status != CreditMemoStatus.Draft && m.Status != CreditMemoStatus.Voided)
                .ToListAsync(ct))
                .GroupBy(m => m.InvoiceId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.Total));

        var net = deposits.Select(i => (Invoice: i, Amount: Math.Max(0m, Money.Round(i.Total) - credited.GetValueOrDefault(i.Id)))).ToList();
        var billed = net.Sum(d => d.Amount);
        var billedFunctional = net.Sum(d => Math.Round(d.Amount * d.Invoice.FxRate, 2, MidpointRounding.AwayFromZero));
        return new DepositPool(
            billed,
            billedFunctional,
            Money.Round(used),
            deposits.Select(i => i.InvoiceNumber).ToList());
    }
}
