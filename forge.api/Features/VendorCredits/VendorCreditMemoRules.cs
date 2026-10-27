using System.Globalization;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.VendorBills;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

/// <summary>
/// The rules every vendor credit memo path shares: numbering, line building, the bill-line and return-line caps and
/// the application checks.
/// </summary>
internal static class VendorCreditMemoRules
{
    public const string NotDraftCode = "vendor-credit-not-draft";
    public const string TotalCode = "vendor-credit-total";
    public const string ExceedsBillLineQuantityCode = "vendor-credit-exceeds-bill-line";
    public const string ExceedsBillLinePriceCode = "vendor-credit-price-exceeds-bill-line";
    public const string BillCode = "vendor-credit-bill";
    public const string ReturnLineCode = "vendor-credit-return-line";
    public const string ExceedsReturnLineCode = "vendor-credit-exceeds-return-line";
    public const string NotOpenCode = "vendor-credit-not-open";
    public const string ExceedsOpenCode = "vendor-credit-exceeds-open";
    public const string ExceedsBalanceCode = "vendor-credit-exceeds-balance";
    public const string AppliedCode = "vendor-credit-applied";
    public const string BillHasCreditsCode = "bill-has-credits";

    private static readonly VendorBillStatus[] ApplicableStatuses =
        [VendorBillStatus.Approved, VendorBillStatus.PartiallyPaid];

    public static ValidationException Failure(string property, string code, string message)
        => CreditMemoRules.Failure(property, code, message);

    public static int UserId(AppDbContext db) => db.CurrentUserId ?? 0;

    public static Task<DateOnly> ShopTodayAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
        => CreditMemoRules.ShopTodayAsync(db, now, ct);

    public static Task<string> NextMemoNumberAsync(AppDbContext db, CancellationToken ct)
        => NextNumberAsync(db, "vendor-credit-memos.number", "VCM-",
            (candidate, token) => db.VendorCreditMemos.IgnoreQueryFilters().AnyAsync(m => m.MemoNumber == candidate, token),
            token => db.VendorCreditMemos.IgnoreQueryFilters().CountAsync(token), ct);

    public static Task<string> NextReturnNumberAsync(AppDbContext db, CancellationToken ct)
        => NextNumberAsync(db, "vendor-returns.number", "RTV-",
            (candidate, token) => db.VendorReturns.IgnoreQueryFilters().AnyAsync(r => r.ReturnNumber == candidate, token),
            token => db.VendorReturns.IgnoreQueryFilters().CountAsync(token), ct);

    public static Task<string> NextRunNumberAsync(AppDbContext db, CancellationToken ct)
        => NextNumberAsync(db, "payment-runs.number", "PR-",
            (candidate, token) => db.PaymentRuns.IgnoreQueryFilters().AnyAsync(r => r.RunNumber == candidate, token),
            token => db.PaymentRuns.IgnoreQueryFilters().CountAsync(token), ct);

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
    /// Replaces the memo's lines with <paramref name="requested"/>, numbered from 1. A line naming a bill line must
    /// belong to the memo's bill and takes that line's part and description when left blank; a line naming a return
    /// line must belong to the memo's return. Explicit GL accounts must be billable.
    /// </summary>
    public static async Task SetLinesAsync(
        AppDbContext db, VendorCreditMemo memo, IReadOnlyList<VendorCreditMemoLineRequestModel> requested, CancellationToken ct)
    {
        var billLineIds = requested.Where(l => l.VendorBillLineId != null).Select(l => l.VendorBillLineId!.Value).Distinct().ToList();
        var billLines = billLineIds.Count == 0
            ? new Dictionary<int, VendorBillLine>()
            : await db.Set<VendorBillLine>().AsNoTracking()
                .Where(l => billLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);
        var returnLineIds = requested.Where(l => l.VendorReturnLineId != null).Select(l => l.VendorReturnLineId!.Value).Distinct().ToList();
        var returnLines = returnLineIds.Count == 0
            ? new Dictionary<int, VendorReturnLine>()
            : await db.VendorReturnLines.AsNoTracking()
                .Where(l => returnLineIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);

        for (var i = 0; i < requested.Count; i++)
        {
            var line = requested[i];
            if (line.VendorBillLineId is int billLineId
                && (!billLines.TryGetValue(billLineId, out var billLine) || memo.VendorBillId != billLine.VendorBillId))
                throw Failure($"Lines[{i}].VendorBillLineId", BillCode, "A credited bill line must belong to the memo's bill.");
            if (line.VendorReturnLineId is int returnLineId
                && (!returnLines.TryGetValue(returnLineId, out var returnLine) || memo.VendorReturnId != returnLine.VendorReturnId))
                throw Failure($"Lines[{i}].VendorReturnLineId", ReturnLineCode, "A credited return line must belong to the memo's return to vendor.");
        }

        await BillableGlAccounts.EnsureBillableAsync(
            db, requested.Where(l => l.GlAccountId is not null && l.VendorBillLineId is null).Select(l => l.GlAccountId!.Value), ct);

        memo.Lines.Clear();
        var number = 1;
        foreach (var l in requested)
        {
            var billLine = l.VendorBillLineId is int id ? billLines[id] : null;
            var returnLine = l.VendorReturnLineId is int rid ? returnLines[rid] : null;
            memo.Lines.Add(new VendorCreditMemoLine
            {
                LineNumber = number++,
                VendorBillLineId = l.VendorBillLineId,
                VendorReturnLineId = l.VendorReturnLineId,
                PartId = l.PartId ?? billLine?.PartId ?? returnLine?.PartId,
                Description = !string.IsNullOrWhiteSpace(l.Description)
                    ? l.Description.Trim()
                    : billLine?.Description ?? "Credit",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                AccountDeterminationKey = returnLine is not null
                    ? VendorCreditMemoPostingService.KeyVendorReturnClearing
                    : billLine?.AccountDeterminationKey
                      ?? (string.IsNullOrWhiteSpace(l.AccountDeterminationKey) ? "OPERATING_EXPENSE" : l.AccountDeterminationKey.Trim()),
                GlAccountId = billLine is null && returnLine is null ? l.GlAccountId : billLine?.GlAccountId,
            });
        }
    }

    /// <summary>
    /// Refuses a memo that credits nothing (<c>vendor-credit-total</c>), a line priced above the bill line it credits
    /// (<c>vendor-credit-price-exceeds-bill-line</c>), and lines that, with every other issued memo, credit more of a
    /// bill line than was billed (<c>vendor-credit-exceeds-bill-line</c>) or more of a return line than went back
    /// (<c>vendor-credit-exceeds-return-line</c>).
    /// </summary>
    public static async Task EnsureIssuableAsync(AppDbContext db, VendorCreditMemo memo, CancellationToken ct)
    {
        if (memo.Total <= 0m)
            throw Failure("Lines", TotalCode, "A vendor credit memo must credit more than zero.");

        var byBillLine = memo.Lines.Where(l => l.VendorBillLineId != null)
            .GroupBy(l => l.VendorBillLineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        if (byBillLine.Count > 0)
        {
            var ids = byBillLine.Keys.ToList();
            var billed = await db.Set<VendorBillLine>().AsNoTracking()
                .Where(l => ids.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => new { l.Quantity, l.UnitPrice, l.LineNumber, l.Description }, ct);
            var already = await db.VendorCreditMemoLines.AsNoTracking()
                .Where(l => l.VendorBillLineId != null && ids.Contains(l.VendorBillLineId.Value)
                    && l.VendorCreditMemoId != memo.Id
                    && l.VendorCreditMemo.Status != VendorCreditMemoStatus.Draft
                    && l.VendorCreditMemo.Status != VendorCreditMemoStatus.Voided)
                .GroupBy(l => l.VendorBillLineId!.Value)
                .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
                .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);

            foreach (var (lineId, lines) in byBillLine)
            {
                if (!billed.TryGetValue(lineId, out var line))
                    continue;
                var overpriced = lines.FirstOrDefault(l => l.UnitPrice > line.UnitPrice);
                if (overpriced is not null)
                    throw Failure("Lines", ExceedsBillLinePriceCode,
                        string.Create(CultureInfo.InvariantCulture,
                            $"Bill line {line.LineNumber} ({line.Description}) was billed at {line.UnitPrice:0.####}; a credit for it can't be priced higher."));
                var before = already.GetValueOrDefault(lineId);
                var quantity = lines.Sum(l => l.Quantity);
                if (before + quantity > line.Quantity)
                    throw Failure("Lines", ExceedsBillLineQuantityCode,
                        string.Create(CultureInfo.InvariantCulture,
                            $"Bill line {line.LineNumber} ({line.Description}) was billed for {line.Quantity:0.####}; {before:0.####} is already credited, so at most {Math.Max(0m, line.Quantity - before):0.####} more can be."));
            }
        }

        var byReturnLine = memo.Lines.Where(l => l.VendorReturnLineId != null)
            .GroupBy(l => l.VendorReturnLineId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (byReturnLine.Count == 0)
            return;

        var returnIds = byReturnLine.Keys.ToList();
        var returned = await db.VendorReturnLines.AsNoTracking()
            .Where(l => returnIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => new { l.Quantity, l.LineNumber }, ct);
        var credited = await db.VendorCreditMemoLines.AsNoTracking()
            .Where(l => l.VendorReturnLineId != null && returnIds.Contains(l.VendorReturnLineId.Value)
                && l.VendorCreditMemoId != memo.Id
                && l.VendorCreditMemo.Status != VendorCreditMemoStatus.Draft
                && l.VendorCreditMemo.Status != VendorCreditMemoStatus.Voided)
            .GroupBy(l => l.VendorReturnLineId!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);
        foreach (var (lineId, quantity) in byReturnLine)
        {
            if (!returned.TryGetValue(lineId, out var line))
                continue;
            var before = credited.GetValueOrDefault(lineId);
            if (before + quantity > line.Quantity)
                throw Failure("Lines", ExceedsReturnLineCode,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Return line {line.LineNumber} sent back {line.Quantity:0.####}; {before:0.####} is already credited, so at most {Math.Max(0m, line.Quantity - before):0.####} more can be."));
        }
    }

    /// <summary>
    /// Applies <paramref name="amount"/> of an open memo to an approved bill of the same vendor and currency, then moves
    /// the bill's status and the memo's status. Returns the new application (not yet saved).
    /// </summary>
    public static async Task<VendorCreditMemoApplication> ApplyAsync(
        AppDbContext db, VendorCreditMemo memo, VendorBill bill, decimal amount, DateTimeOffset now, CancellationToken ct,
        int? vendorPaymentId = null, int? paymentRunId = null)
    {
        if (memo.Status is not (VendorCreditMemoStatus.Open or VendorCreditMemoStatus.PartiallyApplied))
            throw Failure("Id", NotOpenCode, $"Vendor memo {memo.MemoNumber} is {memo.Status}; only an issued memo with an open amount can be applied.");
        if (bill.VendorId != memo.VendorId)
            throw Failure("VendorBillId", BillCode, $"Bill {bill.BillNumber} belongs to a different vendor.");
        if (!ApplicableStatuses.Contains(bill.Status))
            throw Failure("VendorBillId", BillCode,
                $"Bill {bill.BillNumber} is {bill.Status}; credits apply only to Approved or PartiallyPaid bills.");

        var currencies = new DocumentCurrencyResolver(db);
        var memoCurrency = await currencies.EffectiveCurrencyIdAsync(memo.CurrencyId, ct);
        var billCurrency = await currencies.EffectiveCurrencyIdAsync(bill.CurrencyId, ct);
        if (memoCurrency != billCurrency)
            throw Failure("VendorBillId", DocumentCurrencyResolver.PaymentCurrencyMismatchCode,
                $"Bill {bill.BillNumber} is in {await currencies.CodeForAsync(billCurrency, ct)}, the vendor memo in {await currencies.CodeForAsync(memoCurrency, ct)}.");

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        if (amount > memo.OpenAmount)
            throw Failure("Amount", ExceedsOpenCode,
                $"{money.Format(amount)} is more than the {money.Format(memo.OpenAmount)} left on vendor memo {memo.MemoNumber}.");
        if (amount > bill.BalanceDue)
            throw Failure("Amount", ExceedsBalanceCode,
                $"{money.Format(amount)} is more than bill {bill.BillNumber}'s balance of {money.Format(bill.BalanceDue)}.");

        var application = new VendorCreditMemoApplication
        {
            VendorCreditMemoId = memo.Id,
            VendorBillId = bill.Id,
            Amount = amount,
            AppliedAt = now,
            AppliedByUserId = db.CurrentUserId,
            VendorPaymentId = vendorPaymentId,
            PaymentRunId = paymentRunId,
        };
        memo.Applications.Add(application);
        bill.CreditApplications.Add(application);

        PromoteBill(bill);
        memo.RecomputeStatus();
        db.Entry(bill).Property(b => b.Version).IsModified = true;
        return application;
    }

    /// <summary>Moves a bill to Paid when nothing is left due, else to PartiallyPaid, else back to Approved.</summary>
    public static void PromoteBill(VendorBill bill)
    {
        if (bill.Status is VendorBillStatus.Void or VendorBillStatus.Draft)
            return;

        var settled = bill.AmountPaid + bill.DiscountsTaken + bill.CreditsApplied;
        if (bill.BalanceDue <= 0m)
            bill.Status = VendorBillStatus.Paid;
        else if (settled > 0m)
            bill.Status = VendorBillStatus.PartiallyPaid;
        else
            bill.Status = VendorBillStatus.Approved;
    }

    public static Task<VendorBill?> LoadBillAsync(AppDbContext db, int billId, CancellationToken ct)
        => db.VendorBills
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .Include(b => b.CreditApplications)
            .FirstOrDefaultAsync(b => b.Id == billId, ct);

    public static Task<VendorCreditMemo?> LoadMemoAsync(AppDbContext db, int memoId, CancellationToken ct)
        => db.VendorCreditMemos
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .FirstOrDefaultAsync(m => m.Id == memoId, ct);
}
