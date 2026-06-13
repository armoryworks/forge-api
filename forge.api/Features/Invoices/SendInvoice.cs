using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record SendInvoiceCommand(int Id) : IRequest;

/// <summary>
/// Finalizes a Draft invoice (Draft → Sent). This is the invoice-finalize
/// trigger Phase-1 STAGE A wires AR posting to: when CAP-ACCT-FULLGL is enabled,
/// finalizing posts the AR / revenue / tax journal <b>inline, in this command's
/// transaction</b> via <see cref="IInvoiceArPostingService"/> (§7 matrix row 1–2,
/// §8.4). While the capability is OFF (the default) the posting call is a no-op
/// and this handler behaves exactly as it did pre-Phase-1.
/// </summary>
public class SendInvoiceHandler(
    IInvoiceRepository repo,
    // Optional / null-default so the handler stays constructible without an
    // accounting context (e.g. isolated unit tests). The production DI path
    // supplies all three; with CAP-ACCT-FULLGL off the posting service no-ops anyway.
    IInvoiceArPostingService? arPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    // The request-scoped context, used to wrap the status flip + AR posting in one
    // transaction. Null only in isolated unit tests (mocked repo, no context) — then
    // no transaction is opened and behavior is exactly as before.
    AppDbContext? db = null)
    : IRequestHandler<SendInvoiceCommand>
{
    public async Task Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only Draft invoices can be sent");

        invoice.Status = InvoiceStatus.Sent;

        // ── Inline AR posting (Phase-1 STAGE A), wrapped with the Draft→Sent flip in
        // ONE transaction so the journal entry and the status change commit (or roll
        // back) together — the locked inline, single-transaction model (§2). The
        // engine's SaveChanges joins this transaction; the handler commits once at the
        // end, so a posting failure leaves the invoice Draft (no orphaned status flip).
        // No-op while CAP-ACCT-FULLGL is off; the service self-gates. db is null only
        // in isolated unit tests (mocked repo, no context) → no transaction is opened.
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (arPosting is not null)
        {
            var finalizedByUserId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : 0;

            await arPosting.PostInvoiceFinalizedAsync(invoice.Id, finalizedByUserId, cancellationToken);
        }

        await repo.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
