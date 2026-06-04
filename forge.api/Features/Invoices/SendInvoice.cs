using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

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
    // supplies both; with CAP-ACCT-FULLGL off the posting service no-ops anyway.
    IInvoiceArPostingService? arPosting = null,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<SendInvoiceCommand>
{
    public async Task Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only Draft invoices can be sent");

        invoice.Status = InvoiceStatus.Sent;

        // ── Inline AR posting (Phase-1 STAGE A). Built BEFORE the operational
        // SaveChanges so the journal entry and the status change commit (or roll
        // back) together — the locked inline, single-transaction model (§2).
        // No-op while CAP-ACCT-FULLGL is off; the service self-gates.
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
    }
}
