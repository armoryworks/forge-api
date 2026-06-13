using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorBills;

/// <summary>
/// Voids a vendor bill (pre-go-live AP hardening). A <b>Draft</b> bill is a plain cancel (no GL impact). An
/// <b>Approved</b> bill was posted, so voiding it <b>reverses</b> the AP / expense journal (equal-and-opposite
/// entry, original → Reversed) and gives back the billed quantity to its PO lines so the goods can be
/// re-billed — all inline in one transaction (mirrors <see cref="ApproveVendorBillHandler"/>). A bill with
/// payments applied cannot be voided until the payment(s) are voided first (else the reversal would strand
/// the cash side). No-op on the GL while CAP-ACCT-FULLGL is off.
/// </summary>
public record VoidVendorBillCommand(int Id) : IRequest;

public class VoidVendorBillHandler(
    IVendorBillRepository repo,
    IVendorBillApPostingService? apPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null)
    : IRequestHandler<VoidVendorBillCommand>
{
    public async Task Handle(VoidVendorBillCommand request, CancellationToken cancellationToken)
    {
        var bill = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor bill {request.Id} not found");

        if (bill.Status == VendorBillStatus.Void)
            throw new InvalidOperationException("Bill is already void.");

        // A bill promoted from a vendor-settled expense is driven by the EXPENSE lifecycle: voiding it
        // here would strand the expense as approved with its payable (and its GL debit) silently gone.
        // Reject or send the expense back to revision instead — demotion voids the bill in lockstep.
        if (bill.ExpenseId is int sourceExpenseId)
            throw new InvalidOperationException(
                $"Bill {bill.BillNumber} was promoted from expense EXP-{sourceExpenseId}; "
                + "reject the expense (or request revision) to void it.");

        // A bill with vendor payments applied can't be voided — the payment's AP debit would be left pointing
        // at a reversed liability. Void/unapply the payment(s) first. (PartiallyPaid/Paid bills always have
        // applications, so this also blocks them.)
        if (bill.PaymentApplications.Any())
            throw new InvalidOperationException(
                "Cannot void a bill with payments applied; void the payment(s) first.");

        // Only an Approved bill was posted + advanced BilledQuantity (the approval transition). A Draft void
        // is a pure cancel — no reversal, no quantity to restore.
        var wasPosted = bill.Status == VendorBillStatus.Approved;

        bill.Status = VendorBillStatus.Void;

        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (wasPosted)
        {
            if (apPosting is not null)
            {
                var userId = int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid) ? uid : 0;
                await apPosting.ReverseVendorBillApprovedAsync(bill.Id, userId, cancellationToken);
            }

            // Restore operational 3-way-match state (mirror of the approval increment) so the received goods
            // become billable again. Operational — runs regardless of CAP-ACCT-FULLGL.
            foreach (var g in bill.Lines
                         .Where(l => l.PurchaseOrderLineId is not null)
                         .GroupBy(l => l.PurchaseOrderLineId!.Value))
            {
                var poLine = g.First().PurchaseOrderLine;
                if (poLine is not null)
                    poLine.BilledQuantity -= g.Sum(l => l.Quantity);
            }
        }

        // Activity (transactional entity → log on the bill). db is null only in isolated unit tests.
        db?.LogActivityAt(
            "voided",
            $"Bill {bill.BillNumber} voided"
            + (wasPosted
                ? " — ledger entry reversed, PO billed quantities restored"
                : " (draft cancelled)"),
            ("VendorBill", bill.Id));

        await repo.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
