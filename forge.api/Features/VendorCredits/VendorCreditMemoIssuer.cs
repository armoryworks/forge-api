using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

/// <summary>
/// Issues a draft vendor memo: checks it, stamps it Open, posts it, auto-applies it to its bill's balance when it
/// credits one, and moves its return to vendor to Credited (Closed when its NCR is already closed). Runs inside the caller's transaction.
/// </summary>
public class VendorCreditMemoIssuer(AppDbContext db, IVendorCreditMemoPostingService posting, IClock clock)
{
    public async Task IssueAsync(VendorCreditMemo memo, CancellationToken ct)
    {
        if (memo.Status != VendorCreditMemoStatus.Draft)
            throw VendorCreditMemoRules.Failure("Id", VendorCreditMemoRules.NotDraftCode,
                $"Vendor memo {memo.MemoNumber} is {memo.Status}; only a draft can be issued.");

        if (db.Database.CurrentTransaction is not null)
        {
            if (memo.VendorBillId is int lockBillId)
                await db.AcquireAdvisoryLockAsync("vendor-credit-memos.bill", lockBillId.ToString(CultureInfo.InvariantCulture), ct);
            if (memo.VendorReturnId is int lockReturnId)
                await db.AcquireAdvisoryLockAsync("vendor-credit-memos.return", lockReturnId.ToString(CultureInfo.InvariantCulture), ct);
        }
        await VendorCreditMemoRules.EnsureIssuableAsync(db, memo, ct);

        var userId = VendorCreditMemoRules.UserId(db);
        memo.Status = VendorCreditMemoStatus.Open;
        memo.IssuedAt = clock.UtcNow;
        memo.IssuedByUserId = db.CurrentUserId;

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("issued", $"Issued vendor memo {memo.MemoNumber} for {money.Format(memo.Total)}",
            ("VendorCreditMemo", memo.Id), ("Vendor", memo.VendorId));

        if (memo.VendorReturnId is int returnId)
        {
            var rtv = await db.VendorReturns.Include(r => r.NonConformance).FirstOrDefaultAsync(r => r.Id == returnId, ct);
            if (rtv is { Status: VendorReturnStatus.Shipped })
            {
                rtv.Status = rtv.NonConformance?.Status == NcrStatus.Closed ? VendorReturnStatus.Closed : VendorReturnStatus.Credited;
                db.LogActivityAt("credited", $"Vendor credit {memo.MemoNumber} recorded for return {rtv.ReturnNumber}",
                    ("VendorReturn", rtv.Id));
            }
        }
        await db.SaveChangesAsync(ct);

        memo.JournalEntryId = await posting.PostIssueAsync(memo.Id, userId, ct);
        await db.SaveChangesAsync(ct);

        if (memo.VendorBillId is not int billId)
            return;

        var bill = await VendorCreditMemoRules.LoadBillAsync(db, billId, ct);
        if (bill is null || bill.Status is not (VendorBillStatus.Approved or VendorBillStatus.PartiallyPaid) || bill.BalanceDue <= 0m)
            return;

        var amount = Math.Min(memo.OpenAmount, bill.BalanceDue);
        var application = await VendorCreditMemoRules.ApplyAsync(db, memo, bill, amount, clock.UtcNow, ct);
        db.LogActivityAt("credit-applied",
            $"Applied {money.Format(amount)} of vendor memo {memo.MemoNumber} to bill {bill.BillNumber}",
            ("VendorCreditMemo", memo.Id), ("VendorBill", bill.Id));
        await db.SaveChangesAsync(ct);
        await posting.PostApplicationAsync(application.Id, userId, ct);
    }
}
