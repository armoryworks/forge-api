using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorBills;

/// <summary>
/// Approves a Draft <c>VendorBill</c> (Draft → Approved). This is the AP posting trigger (the AP twin of
/// SendInvoice): when CAP-ACCT-FULLGL is enabled, approving posts the AP / expense journal
/// <b>inline, in this command's transaction</b> via <see cref="IVendorBillApPostingService"/>. While the
/// capability is OFF (the default) the posting call is a no-op and the handler just flips the status.
/// </summary>
public record ApproveVendorBillCommand(int Id) : IRequest;

public class ApproveVendorBillHandler(
    IVendorBillRepository repo,
    // Optional / null-default so the handler stays constructible without an accounting context (e.g.
    // isolated unit tests). The production DI path supplies all three; with CAP-ACCT-FULLGL off the
    // posting service no-ops anyway. db is null only in those isolated tests → no transaction is opened.
    IVendorBillApPostingService? apPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<ApproveVendorBillCommand>
{
    public async Task Handle(ApproveVendorBillCommand request, CancellationToken cancellationToken)
    {
        // Load lines + their PO-line refs so both the 3-way-match guard below and the dark posting can see
        // received-but-not-yet-billed quantities.
        var bill = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor bill {request.Id} not found");

        if (bill.Status != VendorBillStatus.Draft)
            throw new InvalidOperationException("Only Draft bills can be approved");

        bill.Status = VendorBillStatus.Approved;

        // ── PO ↔ line invariant (defense-in-depth). CreateVendorBill's validator already enforces this, but
        // re-check it on the posting trigger because a bill can be loaded/mutated outside that path: a
        // PO-matched bill MUST link every line to a PO line, and a standalone bill must link none. A mismatch
        // would silently mis-route the posting — a standalone-routed PO line debits expense instead of
        // clearing the GRNI accrued at receipt, leaving the accrual stranded and BilledQuantity un-advanced.
        if (bill.PurchaseOrderId is not null)
        {
            if (bill.Lines.Any(l => l.PurchaseOrderLineId is null))
                throw new InvalidOperationException(
                    $"Vendor bill {bill.BillNumber} is PO-matched but has a line with no purchase-order line "
                  + "reference (3-way match requires every line to match a PO line).");
        }
        else if (bill.Lines.Any(l => l.PurchaseOrderLineId is not null))
        {
            throw new InvalidOperationException(
                $"Vendor bill {bill.BillNumber} is standalone (no purchase order) but a line references a "
              + "purchase-order line.");
        }

        // ── 3-way match (STAGE D), OPERATIONAL — runs regardless of CAP-ACCT-FULLGL. A PO-linked bill may
        // bill no more than each PO line's received-but-not-yet-billed quantity (else it would clear GRNI it
        // never accrued / pay before receipt); after posting we advance BilledQuantity so a later bill
        // against the same receipt can't double-clear. Several bill lines can hit one PO line → group + sum.
        var poMatches = bill.PurchaseOrderId is not null
            ? bill.Lines
                .Where(l => l.PurchaseOrderLineId is not null)
                .GroupBy(l => l.PurchaseOrderLineId!.Value)
                .Select(g => (PoLine: g.First().PurchaseOrderLine, Quantity: g.Sum(l => l.Quantity)))
                .ToList()
            : new List<(PurchaseOrderLine? PoLine, decimal Quantity)>();

        // Validate the PO-line refs loaded. The over-bill check itself runs after the row lock below, against
        // the freshly-committed BilledQuantity.
        foreach (var (poLine, _) in poMatches)
        {
            if (poLine is null)
                throw new InvalidOperationException(
                    $"Vendor bill {bill.BillNumber} references a purchase-order line that could not be loaded.");
        }

        // ── Inline AP posting wrapped with the status flip in ONE transaction so the journal entry and
        // the Draft→Approved flip commit (or roll back) together (the locked inline model — §2). The
        // engine's SaveChanges joins this transaction; the handler commits once. A posting failure leaves
        // the bill Draft. No-op while CAP-ACCT-FULLGL is off; the service self-gates.
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // ── Concurrency: lock the matched PO-line rows FOR UPDATE so two concurrent approvals against the
        // same line serialize. Without it both could read the same BilledQuantity, both pass the over-bill
        // guard, and both increment — a lost update that double-clears GRNI. Postgres only; a no-op on other
        // providers (InMemory tests). Reload to observe the committed BilledQuantity under the lock.
        if (db is not null && poMatches.Count > 0 && db.Database.IsNpgsql())
        {
            var poLineIds = poMatches.Select(m => m.PoLine!.Id).ToArray();
            await db.Database.ExecuteSqlRawAsync(
                "SELECT id FROM purchase_order_lines WHERE id = ANY({0}) FOR UPDATE", [poLineIds], cancellationToken);
            foreach (var (poLine, _) in poMatches)
                await db.Entry(poLine!).ReloadAsync(cancellationToken);
        }

        // ── Over-bill guard, now against the freshly-locked received-but-not-yet-billed quantity ──
        foreach (var (poLine, quantity) in poMatches)
        {
            if (quantity > poLine!.UnbilledReceivedQuantity)
                throw new InvalidOperationException(
                    $"Vendor bill {bill.BillNumber} bills {quantity} against PO line {poLine.Id}, "
                  + $"but only {poLine.UnbilledReceivedQuantity} is received-but-not-yet-billed.");
        }

        if (apPosting is not null)
        {
            var approvedByUserId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : 0;

            await apPosting.PostVendorBillApprovedAsync(bill.Id, approvedByUserId, cancellationToken);
        }

        // Advance operational 3-way-match state AFTER the posting read the pre-bill BilledQuantity. Persisted
        // by the SaveChanges below within the same transaction (PO lines are tracked by the shared context).
        foreach (var (poLine, quantity) in poMatches)
            poLine!.BilledQuantity += quantity;

        await repo.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
