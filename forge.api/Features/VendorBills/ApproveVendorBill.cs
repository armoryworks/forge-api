using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
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
        var bill = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor bill {request.Id} not found");

        if (bill.Status != VendorBillStatus.Draft)
            throw new InvalidOperationException("Only Draft bills can be approved");

        bill.Status = VendorBillStatus.Approved;

        // ── Inline AP posting wrapped with the status flip in ONE transaction so the journal entry and
        // the Draft→Approved flip commit (or roll back) together (the locked inline model — §2). The
        // engine's SaveChanges joins this transaction; the handler commits once. A posting failure leaves
        // the bill Draft. No-op while CAP-ACCT-FULLGL is off; the service self-gates.
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

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

        await repo.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
