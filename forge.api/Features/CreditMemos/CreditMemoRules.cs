using System.Globalization;
using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.ShopFloor;
using Forge.Api.Middleware;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

/// <summary>The rules every credit memo path shares: numbering, line building, over-credit and application checks.</summary>
internal static class CreditMemoRules
{
    public const string CreditExceedsInvoiceLineCode = "credit-exceeds-invoice-line";
    public const string CreditMemoAppliedCode = "credit-memo-applied";
    public const string InvoiceHasCreditsCode = "invoice-has-credits";
    public const string CreditMemoNotDraftCode = "credit-memo-not-draft";
    public const string CreditMemoTotalCode = "credit-memo-total";
    public const string CreditMemoNotOpenCode = "credit-memo-not-open";
    public const string CreditExceedsOpenCode = "credit-exceeds-open";
    public const string CreditExceedsBalanceCode = "credit-exceeds-balance";
    public const string CreditMemoInvoiceCode = "credit-memo-invoice";
    public const string RefundExceedsOpenCode = "refund-exceeds-open";
    public const string WriteOffExceedsBalanceCode = "writeoff-exceeds-balance";
    public const string WriteOffReservedCode = "credit-memo-write-off-reserved";
    public const string WriteOffRefundCode = "credit-memo-write-off-refund";
    public const string WriteOffApproverMessage = "Only an admin or a manager can apply or unapply a write-off.";

    private static readonly InvoiceStatus[] ApplicableStatuses =
        [InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public static ValidationException Failure(string property, string code, string message)
        => new(message, [new ValidationFailure(property, message) { ErrorCode = code }]);

    public static int UserId(AppDbContext db) => db.CurrentUserId ?? 0;

    /// <summary>
    /// Refuses a write-off reason or line on a memo built through the credit memo endpoints
    /// (<c>credit-memo-write-off-reserved</c>): bad debt is written off only through the invoice write-off, which
    /// needs an admin or a manager.
    /// </summary>
    public static void EnsureNoWriteOff(CreditMemoReasonType reasonType, IReadOnlyList<CreditMemoLineRequestModel> lines)
    {
        if (reasonType == CreditMemoReasonType.WriteOff || lines.Any(l => l.LineType == CreditMemoLineType.WriteOff))
            throw Failure("ReasonType", WriteOffReservedCode,
                "Bad debt is written off from the invoice (Write off balance), not with a credit memo.");
    }

    /// <summary>Refuses applying or unapplying a write-off memo unless the user is an admin or a manager.</summary>
    public static void EnsureWriteOffApprover(CreditMemo memo, ClaimsPrincipal? user)
    {
        if (memo.IsWriteOff && user?.IsInRole("Admin") != true && user?.IsInRole("Manager") != true)
            throw new ForbiddenException(WriteOffApproverMessage);
    }

    public static async Task<DateOnly> ShopTodayAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
        => DateOnly.FromDateTime(ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, ct), now));

    public static Task<string> NextCreditMemoNumberAsync(AppDbContext db, CancellationToken ct)
        => NextNumberAsync(db, "credit-memos.number", "CM-",
            (candidate, token) => db.CreditMemos.IgnoreQueryFilters().AnyAsync(m => m.CreditMemoNumber == candidate, token),
            token => db.CreditMemos.IgnoreQueryFilters().CountAsync(token), ct);

    public static Task<string> NextRefundNumberAsync(AppDbContext db, CancellationToken ct)
        => NextNumberAsync(db, "customer-refunds.number", "RF-",
            (candidate, token) => db.CustomerRefunds.IgnoreQueryFilters().AnyAsync(r => r.RefundNumber == candidate, token),
            token => db.CustomerRefunds.IgnoreQueryFilters().CountAsync(token), ct);

    private static async Task<string> NextNumberAsync(
        AppDbContext db, string lockScope, string prefix,
        Func<string, CancellationToken, Task<bool>> exists, Func<CancellationToken, Task<int>> count, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.AcquireAdvisoryLockAsync(lockScope, string.Empty, ct);

        var next = await count(ct) + 1;
        while (true)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:D6}");
            if (!await exists(candidate, ct))
                return candidate;
            next++;
        }
    }

    /// <summary>
    /// Replaces the memo's lines with <paramref name="requested"/>, numbered from 1. A line naming an invoice line
    /// must belong to the memo's invoice; it takes that line's part, description and taxability when left blank.
    /// </summary>
    public static async Task SetLinesAsync(
        AppDbContext db, CreditMemo memo, IReadOnlyList<CreditMemoLineRequestModel> requested, CancellationToken ct)
    {
        var invoiceLineIds = requested.Where(l => l.InvoiceLineId != null).Select(l => l.InvoiceLineId!.Value).Distinct().ToList();
        var invoiceLines = invoiceLineIds.Count == 0
            ? new Dictionary<int, InvoiceLine>()
            : await db.InvoiceLines.AsNoTracking()
                .Where(l => invoiceLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);

        for (var i = 0; i < requested.Count; i++)
        {
            if (requested[i].InvoiceLineId is not int lineId)
                continue;
            if (!invoiceLines.TryGetValue(lineId, out var line) || memo.InvoiceId != line.InvoiceId)
                throw Failure($"Lines[{i}].InvoiceLineId", CreditMemoInvoiceCode,
                    "A credited invoice line must belong to the credit memo's invoice.");
        }

        memo.Lines.Clear();
        var number = 1;
        foreach (var l in requested)
        {
            var source = l.InvoiceLineId is int id ? invoiceLines[id] : null;
            var taxable = l.Taxable ?? (source?.IsTaxable ?? l.LineType != CreditMemoLineType.WriteOff);
            memo.Lines.Add(new CreditMemoLine
            {
                LineNumber = number++,
                LineType = l.LineType,
                PartId = l.PartId ?? source?.PartId,
                InvoiceLineId = l.InvoiceLineId,
                Description = !string.IsNullOrWhiteSpace(l.Description)
                    ? l.Description.Trim()
                    : source?.Description ?? DefaultDescription(l.LineType),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                TaxCode = taxable && l.LineType != CreditMemoLineType.WriteOff ? null : InvoiceLine.NonTaxableCode,
            });
        }
    }

    public static string DefaultDescription(CreditMemoLineType type) => type switch
    {
        CreditMemoLineType.Freight => "Freight",
        CreditMemoLineType.WriteOff => "Balance written off",
        CreditMemoLineType.RestockingFee => "Restocking fee",
        _ => "Credit",
    };

    /// <summary>
    /// Refuses a memo whose lines, together with every other issued memo, credit more of an invoice line than was
    /// invoiced (<c>credit-exceeds-invoice-line</c>), and a memo that credits nothing (<c>credit-memo-total</c>).
    /// </summary>
    public static async Task EnsureIssuableAsync(AppDbContext db, CreditMemo memo, CancellationToken ct)
    {
        var items = memo.Lines.Where(l => l.LineType is CreditMemoLineType.Item or CreditMemoLineType.RestockingFee).Sum(l => l.SignedTotal);
        if (memo.Total <= 0m || items < 0m)
            throw Failure("Lines", CreditMemoTotalCode,
                "A credit memo must credit more than zero, and a restocking fee can't exceed the goods credited.");

        var credited = memo.Lines
            .Where(l => l.InvoiceLineId != null && l.LineType is CreditMemoLineType.Item or CreditMemoLineType.Freight)
            .GroupBy(l => l.InvoiceLineId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (credited.Count == 0)
            return;

        var ids = credited.Keys.ToList();
        var invoiced = await db.InvoiceLines.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => new { l.Quantity, l.LineNumber, l.Description }, ct);
        var alreadyCredited = await db.CreditMemoLines.AsNoTracking()
            .Where(l => l.InvoiceLineId != null && ids.Contains(l.InvoiceLineId.Value)
                && l.CreditMemoId != memo.Id
                && (l.LineType == CreditMemoLineType.Item || l.LineType == CreditMemoLineType.Freight)
                && l.CreditMemo.Status != CreditMemoStatus.Draft && l.CreditMemo.Status != CreditMemoStatus.Voided)
            .GroupBy(l => l.InvoiceLineId!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);

        foreach (var (lineId, quantity) in credited)
        {
            if (!invoiced.TryGetValue(lineId, out var line))
                continue;
            var before = alreadyCredited.GetValueOrDefault(lineId);
            if (before + quantity > line.Quantity)
                throw Failure("Lines", CreditExceedsInvoiceLineCode,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Invoice line {line.LineNumber} ({line.Description}) was invoiced for {line.Quantity:0.####}; {before:0.####} is already credited, so at most {Math.Max(0m, line.Quantity - before):0.####} more can be."));
        }
    }

    /// <summary>
    /// Applies <paramref name="amount"/> of an issued memo to an invoice of the same customer and currency, then moves
    /// the invoice's status and the memo's status. Returns the new application (not yet saved).
    /// </summary>
    public static async Task<CreditMemoApplication> ApplyAsync(
        AppDbContext db, CreditMemo memo, Invoice invoice, decimal amount, DateTimeOffset now, CancellationToken ct)
    {
        if (memo.Status is not (CreditMemoStatus.Issued or CreditMemoStatus.PartiallyApplied))
            throw Failure("Id", CreditMemoNotOpenCode, $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only an issued memo with an open amount can be applied.");
        if (invoice.CustomerId != memo.CustomerId)
            throw Failure("InvoiceId", CreditMemoInvoiceCode, $"Invoice {invoice.InvoiceNumber} belongs to a different customer.");
        if (!ApplicableStatuses.Contains(invoice.Status))
            throw Failure("InvoiceId", CreditMemoInvoiceCode,
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; credits apply only to Sent, PartiallyPaid or Overdue invoices.");

        var currencies = new DocumentCurrencyResolver(db);
        var memoCurrency = await currencies.EffectiveCurrencyIdAsync(memo.CurrencyId, ct);
        var invoiceCurrency = await currencies.EffectiveCurrencyIdAsync(invoice.CurrencyId, ct);
        if (memoCurrency != invoiceCurrency)
            throw Failure("InvoiceId", DocumentCurrencyResolver.PaymentCurrencyMismatchCode,
                $"Invoice {invoice.InvoiceNumber} is in {await currencies.CodeForAsync(invoiceCurrency, ct)}, the credit memo in {await currencies.CodeForAsync(memoCurrency, ct)}.");

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        if (amount > memo.OpenAmount)
            throw Failure("Amount", CreditExceedsOpenCode,
                $"{money.Format(amount)} is more than the {money.Format(memo.OpenAmount)} left on credit memo {memo.CreditMemoNumber}.");
        if (amount > invoice.BalanceDue)
            throw Failure("Amount", CreditExceedsBalanceCode,
                $"{money.Format(amount)} is more than invoice {invoice.InvoiceNumber}'s balance of {money.Format(invoice.BalanceDue)}.");

        var application = new CreditMemoApplication
        {
            CreditMemoId = memo.Id,
            InvoiceId = invoice.Id,
            Amount = amount,
            AppliedAt = now,
            AppliedByUserId = db.CurrentUserId,
        };
        memo.Applications.Add(application);
        invoice.CreditApplications.Add(application);

        PromoteInvoice(invoice);
        memo.RecomputeStatus();
        db.Entry(invoice).Property(i => i.Version).IsModified = true;
        return application;
    }

    /// <summary>Moves an invoice to Paid when nothing is left due, else to PartiallyPaid, else back to Sent.</summary>
    public static void PromoteInvoice(Invoice invoice)
    {
        if (invoice.Status is InvoiceStatus.Voided or InvoiceStatus.Draft)
            return;

        var balance = invoice.BalanceDue;
        var settled = invoice.AmountPaid + invoice.DiscountsTaken + invoice.CreditsApplied;
        if (balance <= 0m)
            invoice.Status = InvoiceStatus.Paid;
        else if (settled > 0m)
            invoice.Status = InvoiceStatus.PartiallyPaid;
        else if (invoice.Status == InvoiceStatus.Paid || invoice.Status == InvoiceStatus.PartiallyPaid)
            invoice.Status = InvoiceStatus.Sent;
    }

    public static Task<Invoice?> LoadInvoiceAsync(AppDbContext db, int invoiceId, CancellationToken ct)
        => db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Include(i => i.CreditApplications)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

    public static Task<CreditMemo?> LoadMemoAsync(AppDbContext db, int creditMemoId, CancellationToken ct)
        => db.CreditMemos
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Include(m => m.Refunds)
            .FirstOrDefaultAsync(m => m.Id == creditMemoId, ct);
}
