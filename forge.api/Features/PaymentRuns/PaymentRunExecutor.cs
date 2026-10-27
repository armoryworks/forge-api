using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorCredits;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentRuns;

/// <summary>
/// Pays a payment run vendor by vendor: one <see cref="CreateVendorPaymentCommand"/> per vendor and currency (its own
/// transaction, so segregation of duties, the cash posting and ACH batching work as for a payment entered by hand),
/// with the vendor's picked credits deducted in the same transaction. A vendor whose credits cover its whole run gets
/// no payment; its credits are applied on their own. Items are marked Paid as each vendor commits. A failure stops the
/// run as Interrupted with the failing vendor's items Failed; the vendors already paid stay paid. Running it again
/// skips every vendor already paid (a payment, or credits, that name the run), so a resume pays only the rest.
/// </summary>
public class PaymentRunExecutor(AppDbContext db, ISender sender, IClock clock, IVendorCreditMemoPostingService? creditPosting = null)
{
    /// <summary>Pays what is left of the run; returns the vendor payments this call created.</summary>
    public async Task<List<VendorPaymentListItemModel>> ExecuteAsync(int runId, CancellationToken ct)
    {
        var created = new List<VendorPaymentListItemModel>();
        var run = await LoadAsync(runId, ct);
        if (run.Status is PaymentRunStatus.Completed or PaymentRunStatus.Cancelled)
            return created;

        run.Status = PaymentRunStatus.Running;
        run.StartedAt ??= clock.UtcNow;
        run.LastError = null;
        foreach (var failed in run.Items.Where(i => i.Status == PaymentRunItemStatus.Failed))
        {
            failed.Status = PaymentRunItemStatus.Pending;
            failed.ErrorMessage = null;
        }
        await db.SaveChangesAsync(ct);

        var groups = run.Items
            .Where(i => i.Status != PaymentRunItemStatus.Paid)
            .GroupBy(i => (i.VendorId, i.CurrencyId))
            .OrderBy(g => g.Key.VendorId).ThenBy(g => g.Key.CurrencyId)
            .Select(g => g.Key)
            .ToList();

        foreach (var (vendorId, currencyId) in groups)
        {
            try
            {
                if (await PayVendorAsync(runId, vendorId, currencyId, ct) is { } payment)
                    created.Add(payment);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                run = await LoadAsync(runId, ct);
                var message = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                foreach (var item in run.Items.Where(i => i.VendorId == vendorId && i.CurrencyId == currencyId && i.Status != PaymentRunItemStatus.Paid))
                {
                    item.Status = PaymentRunItemStatus.Failed;
                    item.ErrorMessage = message;
                    item.ProcessedAt = clock.UtcNow;
                }
                run.Status = PaymentRunStatus.Interrupted;
                run.LastError = message;
                db.LogActivityAt("payment-run-interrupted", $"Payment run {run.RunNumber} interrupted: {message}", ("PaymentRun", run.Id));
                await db.SaveChangesAsync(ct);
                return created;
            }
        }

        db.ChangeTracker.Clear();
        run = await LoadAsync(runId, ct);
        run.Status = PaymentRunStatus.Completed;
        run.CompletedAt = clock.UtcNow;
        db.LogActivityAt("payment-run-completed",
            string.Create(CultureInfo.InvariantCulture, $"Payment run {run.RunNumber} completed: {run.Items.Count} bills paid"),
            ("PaymentRun", run.Id));
        await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<VendorPaymentListItemModel?> PayVendorAsync(int runId, int vendorId, int currencyId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var run = await LoadAsync(runId, ct);
        var items = run.Items
            .Where(i => i.VendorId == vendorId && i.CurrencyId == currencyId && i.Status != PaymentRunItemStatus.Paid)
            .OrderBy(i => i.Id)
            .ToList();
        if (items.Count == 0)
            return null;
        var billIds = items.Select(i => i.VendorBillId).ToList();

        var existingPayment = await db.VendorPayments.AsNoTracking()
            .Where(p => p.PaymentRunId == runId && p.VendorId == vendorId && p.CurrencyId == currencyId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        var creditsDone = existingPayment is null && await db.VendorCreditMemoApplications.AsNoTracking()
            .AnyAsync(a => a.PaymentRunId == runId && a.VendorPaymentId == null && a.ReversedAt == null && billIds.Contains(a.VendorBillId), ct);
        if (existingPayment is not null || creditsDone)
        {
            await MarkPaidAsync(run, items, existingPayment, ct);
            return null;
        }

        var credits = items.SelectMany(i => i.Credits).ToList();
        var applications = items
            .Select(i => (Item: i, Cash: Math.Max(0m, i.Amount - i.Credits.Sum(c => c.Amount)), Discount: i.DiscountAmount ?? 0m))
            .Where(x => x.Cash > 0m || x.Discount > 0m)
            .Select(x => new CreateVendorPaymentApplicationModel(x.Item.VendorBillId, x.Cash, DiscountAmount: x.Item.DiscountAmount))
            .ToList();
        var cash = applications.Sum(a => a.Amount);

        if (cash > 0m)
        {
            var payment = await sender.Send(new CreateVendorPaymentCommand(
                vendorId, run.Method, cash, run.PaymentDate, run.ReferenceNumber, run.Notes, applications,
                CurrencyId: currencyId,
                Credits: credits.Select(c => new CreateVendorPaymentCreditModel(c.VendorCreditMemoId, c.PaymentRunItem.VendorBillId, c.Amount)).ToList(),
                PaymentRunId: runId), ct);
            db.ChangeTracker.Clear();
            run = await LoadAsync(runId, ct);
            items = run.Items.Where(i => billIds.Contains(i.VendorBillId)).ToList();
            await MarkPaidAsync(run, items, payment.Id, ct);
            return payment;
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var posted = new List<VendorCreditMemoApplication>();
        foreach (var credit in credits)
        {
            var memo = await VendorCreditMemoRules.LoadMemoAsync(db, credit.VendorCreditMemoId, ct)
                ?? throw new KeyNotFoundException($"Vendor credit memo {credit.VendorCreditMemoId} not found");
            var bill = await VendorCreditMemoRules.LoadBillAsync(db, credit.PaymentRunItem.VendorBillId, ct)
                ?? throw new KeyNotFoundException($"Vendor bill {credit.PaymentRunItem.VendorBillId} not found");
            var application = await VendorCreditMemoRules.ApplyAsync(db, memo, bill, credit.Amount, run.PaymentDate, ct, paymentRunId: runId);
            db.LogActivityAt("credit-applied",
                $"Payment run {run.RunNumber} applied vendor memo {memo.MemoNumber} to bill {bill.BillNumber}",
                ("VendorCreditMemo", memo.Id), ("VendorBill", bill.Id), ("PaymentRun", run.Id));
            await db.SaveChangesAsync(ct);
            credit.VendorCreditMemoApplicationId = application.Id;
            posted.Add(application);
        }
        if (creditPosting is not null)
            foreach (var application in posted)
                await creditPosting.PostApplicationAsync(application.Id, VendorCreditMemoRules.UserId(db), ct);
        await MarkPaidAsync(run, items, null, ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return null;
    }

    private async Task MarkPaidAsync(PaymentRun run, List<PaymentRunItem> items, int? paymentId, CancellationToken ct)
    {
        var billIds = items.Select(i => i.VendorBillId).ToList();
        var applications = await db.VendorCreditMemoApplications.AsNoTracking()
            .Where(a => a.PaymentRunId == run.Id && a.ReversedAt == null && billIds.Contains(a.VendorBillId))
            .Select(a => new { a.Id, a.VendorCreditMemoId, a.VendorBillId })
            .ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var item in items)
        {
            item.Status = PaymentRunItemStatus.Paid;
            item.VendorPaymentId = paymentId;
            item.ErrorMessage = null;
            item.ProcessedAt = now;
            foreach (var credit in item.Credits.Where(c => c.VendorCreditMemoApplicationId is null))
                credit.VendorCreditMemoApplicationId = applications
                    .FirstOrDefault(a => a.VendorCreditMemoId == credit.VendorCreditMemoId && a.VendorBillId == item.VendorBillId)?.Id;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<PaymentRun> LoadAsync(int runId, CancellationToken ct)
        => await db.PaymentRuns
            .Include(r => r.Items).ThenInclude(i => i.Credits)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
           ?? throw new KeyNotFoundException($"Payment run {runId} not found");
}
