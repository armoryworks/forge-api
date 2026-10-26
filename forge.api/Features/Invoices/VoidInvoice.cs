using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.CustomerReturns;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Invoices;

public record VoidInvoiceCommand(int Id) : IRequest;

/// <summary>
/// Voids a sent invoice. When CAP-ACCT-FULLGL is on, the void also reverses everything the invoice posted
/// (AR / revenue / tax, the delivery reclass and the COGS / finished-goods relief) and voids its AR open item,
/// inline in one transaction with the status flip (mirrors <see cref="VendorBills.VoidVendorBillHandler"/>).
/// An invoice with payments applied cannot be voided until the payments are voided first; nor can one with issued
/// credit memos against it, or with returned goods restocked or scrapped against it (credit it instead).
/// </summary>
public class VoidInvoiceHandler(
    IInvoiceRepository repo,
    IInvoiceArPostingService? arPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    AppDbContext? db = null,
    IClock? clock = null)
    : IRequestHandler<VoidInvoiceCommand>
{
    // F-033: source-state whitelist — Draft invoices should be deleted not voided;
    // PartiallyPaid is allowed here (payment guard below still catches applied payments)
    private static readonly HashSet<InvoiceStatus> _voidableStatuses =
        [InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public async Task Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        if (!_voidableStatuses.Contains(invoice.Status))
            throw new InvalidOperationException(
                $"Cannot void an invoice in status {invoice.Status}. Allowed: Sent, PartiallyPaid, Overdue.");

        if (invoice.PaymentApplications.Any())
            throw new InvalidOperationException("Cannot void an invoice with payments applied");

        if (invoice.CreditApplications.Any(a => a.ReversedAt == null))
            throw CreditMemoRules.Failure("Id", CreditMemoRules.InvoiceHasCreditsCode,
                $"Invoice {invoice.InvoiceNumber} has credit memos applied. Unapply them before voiding the invoice.");

        if (db is not null && await db.CreditMemos.AnyAsync(m => m.InvoiceId == invoice.Id
                && m.Status != CreditMemoStatus.Draft && m.Status != CreditMemoStatus.Voided, cancellationToken))
            throw CreditMemoRules.Failure("Id", CreditMemoRules.InvoiceHasCreditsCode,
                $"Invoice {invoice.InvoiceNumber} has issued credit memos, which already reverse part of its sale. Void them before voiding the invoice.");

        if (db is not null && await db.CustomerReturns.AnyAsync(r => r.InvoiceId == invoice.Id
                && (r.Disposition == ReturnDisposition.ReturnToStock || r.Disposition == ReturnDisposition.Scrap), cancellationToken))
            throw CreditMemoRules.Failure("Id", ReturnResolveCodes.InvoiceHasDisposedReturn,
                $"Goods returned against invoice {invoice.InvoiceNumber} were restocked or scrapped, so voiding it would reverse their cost of goods twice. Credit the invoice instead.");

        if (invoice.Provider == ConversionOpenItems.Provider)
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} carries an opening balance brought over at conversion, so voiding it "
                + "would leave that balance on the receivables account. Correct the opening balance with a journal entry instead.");

        if (db is not null)
            await CustomerDeposits.EnsureVoidableAsync(db, invoice, cancellationToken);

        invoice.Status = InvoiceStatus.Voided;

        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (arPosting is not null)
        {
            var userId = int.TryParse(
                httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var uid) ? uid : 0;
            var now = clock?.UtcNow ?? invoice.InvoiceDate;
            var shopZone = db is not null
                ? await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken)
                : TimeZoneInfo.Utc;
            var voidDate = DateOnly.FromDateTime(ClockStateRules.LocalToday(shopZone, now));

            await arPosting.ReverseInvoiceFinalizedAsync(invoice.Id, voidDate, userId, cancellationToken);
        }

        db?.LogActivityAt("voided", $"Voided invoice {invoice.InvoiceNumber}", ("Invoice", invoice.Id));

        await repo.SaveChangesAsync(cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
