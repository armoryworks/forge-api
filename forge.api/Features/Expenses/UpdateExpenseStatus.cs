using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record UpdateExpenseStatusCommand(int Id, UpdateExpenseStatusRequestModel Data) : IRequest<ExpenseResponseModel>;

public class UpdateExpenseStatusValidator : AbstractValidator<UpdateExpenseStatusCommand>
{
    private const int DeclineNoteMinLength = 10;

    public UpdateExpenseStatusValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Status).IsInEnum();
        RuleFor(x => x.Data.ApprovalNotes).MaximumLength(1000).When(x => x.Data.ApprovalNotes is not null);
        RuleFor(x => x.Data.ApprovalNotes)
            .NotEmpty()
            .MinimumLength(DeclineNoteMinLength)
            .When(x => x.Data.Status is ExpenseStatus.Rejected or ExpenseStatus.NeedsRevision)
            .WithMessage($"A note of at least {DeclineNoteMinLength} characters is required when rejecting or requesting revision.");
    }
}

public class UpdateExpenseStatusHandler(
    IExpenseRepository repo,
    IHttpContextAccessor httpContext,
    ISyncQueueRepository syncQueue,
    IAccountingProviderFactory providerFactory,
    ILogger<UpdateExpenseStatusHandler> logger,
    // Optional / null-default so the handler stays constructible without an
    // accounting context (e.g. isolated unit tests). The production DI path
    // supplies it; with CAP-ACCT-FULLGL off the posting service no-ops anyway
    // (mirrors SendInvoice's STAGE A / CreatePayment's STAGE B wiring).
    IExpenseApPostingService? apPosting = null,
    // Promotes a vendor-settled expense into the AP bill pipeline on approval (and demotes —
    // voids the bill — when it leaves approved status). Optional like apPosting; when absent or
    // when promotion declines (Payables capability off / cash-settled), apPosting is the fallback.
    IExpenseBillPromotionService? billPromotion = null,
    // The request-scoped context, used to wrap the status change + AP posting in one
    // transaction. Null only in isolated unit tests (mocked repo, no context) — then
    // no transaction is opened and behavior is exactly as before.
    AppDbContext? db = null) : IRequestHandler<UpdateExpenseStatusCommand, ExpenseResponseModel>
{
    public async Task<ExpenseResponseModel> Handle(UpdateExpenseStatusCommand request, CancellationToken cancellationToken)
    {
        var expense = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Expense not found.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        expense.Status = request.Data.Status;
        expense.ApprovedBy = userId;
        expense.ApprovalNotes = request.Data.ApprovalNotes?.Trim();

        // ── Inline expense / AP posting (Phase-1 STAGE C, §7 matrix row "Expense
        // approved"), wrapped with the status change in ONE transaction so the
        // expense update and the journal entry commit (or roll back) together — the
        // locked inline, single-transaction model (§2). The engine's SaveChanges
        // joins this transaction; the handler commits once, so a posting failure
        // leaves the expense status unchanged (no orphaned approval). Posting runs on
        // the approved transition, on the SAME request-scoped context. No-op while
        // CAP-ACCT-FULLGL is off; the service self-gates, so the operational
        // expense-approval flow is unchanged while dark. Dr Expense / Cr AP
        // (party = vendor) when the expense settles to a vendor, else Cr Cash.
        // db is null only in isolated unit tests (mocked repo) → no transaction.
        await using var tx = db is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await repo.SaveChangesAsync(cancellationToken);

        if (request.Data.Status is ExpenseStatus.Approved or ExpenseStatus.SelfApproved)
        {
            // Promotion first: a vendor-settled expense becomes a real vendor bill (open item, aging,
            // payable via vendor payments). Promotion handles the GL through the bill posting; only
            // when it declines (Payables off / cash-settled / no vendor) does the legacy expense AP
            // posting fire — both inside this transaction.
            var promotedBill = billPromotion is not null
                ? await billPromotion.PromoteApprovedExpenseAsync(expense.Id, userId, cancellationToken)
                : null;

            if (promotedBill is null && apPosting is not null)
                await apPosting.PostExpenseApprovedAsync(expense.Id, userId, cancellationToken);
        }
        else if (billPromotion is not null)
        {
            // Leaving approved status (rejected / revision / re-opened): void the promoted bill and
            // reverse its posting. Throws — blocking the transition — if payments are applied.
            await billPromotion.DemoteExpenseBillAsync(expense.Id, userId, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        // Enqueue QB expense creation when approved
        if (request.Data.Status is ExpenseStatus.Approved or ExpenseStatus.SelfApproved)
        {
            try
            {
                var accountingService = await providerFactory.GetActiveProviderAsync(cancellationToken);
                if (accountingService is not null)
                {
                    var syncStatus = await accountingService.GetSyncStatusAsync(cancellationToken);
                    if (syncStatus.Connected)
                    {
                        // F-26B-02: carry the vendor through to QBO so a vendor-settled
                        // expense syncs as a purchase against that vendor (not a vendorless
                        // cash purchase). The expense loaded by FindAsync doesn't Include the
                        // Vendor nav, so resolve the vendor's ExternalId on demand. Null when
                        // the expense has no vendor or the vendor isn't yet synced to QBO —
                        // unchanged (vendorless) behavior.
                        var vendorExternalId = await ResolveVendorExternalIdAsync(expense, cancellationToken);

                        var accountingExpense = new AccountingExpense(
                            VendorExternalId: vendorExternalId,
                            CustomerExternalId: null,
                            Amount: expense.Amount,
                            Date: expense.ExpenseDate,
                            Description: expense.Description,
                            Category: expense.Category,
                            RefNumber: $"EXP-{expense.Id}");
                        var payload = JsonSerializer.Serialize(accountingExpense);
                        await syncQueue.EnqueueAsync("Expense", expense.Id, "CreateExpense", payload, cancellationToken);
                        logger.LogInformation("Enqueued CreateExpense sync for Expense {ExpenseId}", expense.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enqueue expense sync for Expense {ExpenseId} — continuing", expense.Id);
            }
        }

        return (await repo.GetByIdAsync(expense.Id, cancellationToken))!;
    }

    /// <summary>
    /// The QBO <see cref="Vendor.ExternalId"/> for a vendor-settled expense, or null
    /// (out-of-pocket / cash expense, or a vendor not yet synced to the provider).
    /// Prefers the already-loaded <see cref="Expense.Vendor"/> nav; falls back to a
    /// read-only lookup by <see cref="Expense.VendorId"/> since the approve path's
    /// expense (from FindAsync) doesn't Include the vendor. Returns null when there is
    /// no AppDbContext (isolated unit tests) and the nav isn't already populated.
    /// </summary>
    private async Task<string?> ResolveVendorExternalIdAsync(Expense expense, CancellationToken cancellationToken)
    {
        if (expense.VendorId is null)
            return null;

        if (expense.Vendor is not null)
            return expense.Vendor.ExternalId;

        if (db is null)
            return null;

        return await db.Vendors
            .AsNoTracking()
            .Where(v => v.Id == expense.VendorId.Value)
            .Select(v => v.ExternalId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
