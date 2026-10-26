using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.PaymentSchedules;
using Forge.Api.Features.PaymentTerms;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Payments;

public record CreatePaymentCommand(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications,
    // Optional caller-supplied payment number — see CreatePaymentRequestModel.PaymentNumber.
    string? PaymentNumber = null,
    int? CurrencyId = null,
    decimal? FxRate = null) : IRequest<PaymentListItemModel>;

public class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.FxRate).GreaterThan(0m).When(x => x.FxRate.HasValue);
        RuleFor(x => x.Method).NotEmpty()
            // Constrain to the PaymentMethod enum so an invalid string is a 400, not a 500 from the
            // handler's Enum.Parse (parity with CreateVendorPayment — Phase-2 review).
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Method must be one of: Cash, Check, CreditCard, BankTransfer, Wire, Other");
        When(x => x.Applications != null && x.Applications.Count > 0, () =>
        {
            RuleFor(x => x.Applications!.Sum(a => a.Amount))
                .LessThanOrEqualTo(x => x.Amount)
                .WithMessage("Applied amounts cannot exceed payment amount");
            // A duplicate invoice reference would bypass the per-invoice over-apply guard (EF returns the
            // same tracked entity, so each check sees the same pre-application balance) — reject duplicates.
            RuleFor(x => x.Applications!)
                .Must(a => a.Select(x => x.InvoiceId).Distinct().Count() == a.Count)
                .WithMessage("An invoice may be referenced at most once per payment");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.InvoiceId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
                // Settlement FX rate must be positive (a 0 or negative rate would zero/invert the functional
                // cash amount, breaking the realized-FX plug). Default 1 satisfies this for single-currency.
                app.RuleFor(a => a.SettlementFxRate).GreaterThan(0m).When(a => a.SettlementFxRate.HasValue);
                app.RuleFor(a => a.DiscountAmount).GreaterThanOrEqualTo(0m).MoneyScale().When(a => a.DiscountAmount.HasValue);
            });
        });
    }
}

public class CreatePaymentHandler(
    IPaymentRepository repo,
    ICustomerRepository customerRepo,
    IInvoiceRepository invoiceRepo,
    AppDbContext db,
    // Optional / null-default so the handler stays constructible without an
    // accounting context (e.g. isolated unit tests). The production DI path
    // supplies both; with CAP-ACCT-FULLGL off the posting service no-ops anyway
    // (mirrors SendInvoice's STAGE A wiring).
    IPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    // AUDIT-21-S1: same optional/null-default pattern — DI supplies both; the QBO enqueue self-gates
    // on a provider being connected.
    IAccountingProviderFactory? accountingProviders = null,
    ISyncQueueRepository? syncQueue = null,
    // Optional / null-default (same reason): a manual-number override needs the setting repo, and the
    // identifier registry records the number for resolution/history. DI supplies both.
    ISystemSettingRepository? systemSettings = null,
    IBusinessIdentifierService? identifiers = null)
    : IRequestHandler<CreatePaymentCommand, PaymentListItemModel>
{
    // System setting that gates caller-supplied payment numbers. Stored as "true"/"false".
    private const string AllowManualPaymentNumbersKey = "payments.allow_manual_numbers";

    public async Task<PaymentListItemModel> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        // One unit of work: the payment + its applications + the invoice-status
        // updates AND the inline cash-receipt posting commit (or roll back) together
        // — the locked inline, single-transaction model (§2). The engine's
        // SaveChanges joins this transaction instead of committing on its own, so a
        // posting failure unwinds the payment too (no orphaned operational row). On
        // Npgsql this is a real transaction; on the in-memory test provider it's an
        // ignored no-op, so the mock-based handler tests are unaffected.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var paymentNumber = await ResolvePaymentNumberAsync(request, cancellationToken);
        var method = Enum.Parse<PaymentMethod>(request.Method, true);

        var firstInvoiceId = request.Applications?.FirstOrDefault()?.InvoiceId;
        var firstInvoiceCurrencyId = firstInvoiceId is int invoiceId
            ? await db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => (int?)i.CurrencyId).FirstOrDefaultAsync(cancellationToken)
            : null;
        var currency = await new DocumentCurrencyResolver(db).ResolveAsync(
            request.CurrencyId, request.FxRate, firstInvoiceCurrencyId ?? customer.CurrencyId, request.PaymentDate, cancellationToken);

        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            CustomerId = request.CustomerId,
            CurrencyId = currency.CurrencyId,
            FxRate = currency.FxRate,
            Method = method,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
        };

        decimal appliedTotal = 0;
        // SOs whose invoice(s) became fully paid in this payment — completion candidates.
        var completionCandidateSoIds = new HashSet<int>();

        if (request.Applications != null)
        {
            var discounts = await DiscountSettings.ReadAsync(db, cancellationToken);
            var user = httpContextAccessor?.HttpContext?.User;
            for (var index = 0; index < request.Applications.Count; index++)
            {
                var app = request.Applications[index];
                var invoice = await invoiceRepo.FindWithDetailsAsync(app.InvoiceId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Invoice {app.InvoiceId} not found");

                await PaymentApplicationRules.ApplyToInvoiceAsync(
                    db, payment, invoice, app, completionCandidateSoIds, discounts, user,
                    $"Applications[{index}].DiscountAmount", cancellationToken);
                appliedTotal += app.Amount;
            }
        }

        await repo.AddAsync(payment, cancellationToken);

        try
        {
            await repo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A concurrent payment committed to this invoice between our read and write.
            // Reload each conflicted invoice (including navigations for balance calc) and
            // re-run the over-apply guard; throws InvalidOperationException → 409.
            foreach (var entry in ex.Entries.Where(e => e.Entity is Invoice))
            {
                await entry.ReloadAsync(cancellationToken);
                var inv = (Invoice)entry.Entity;
                await db.Entry(inv).Collection(i => i.PaymentApplications).LoadAsync(cancellationToken);
                await db.Entry(inv).Collection(i => i.Lines).LoadAsync(cancellationToken);

                var freshBalance = inv.BalanceDue;

                var matchingApp = payment.Applications.First(a => a.InvoiceId == inv.Id);
                if (matchingApp.Amount + matchingApp.DiscountAmount > freshBalance)
                {
                    var money = await MoneyFormat.ForCurrencyIdAsync(db, inv.CurrencyId, cancellationToken);
                    throw new InvalidOperationException(
                        $"Application amount {money.Format(matchingApp.Amount)} exceeds invoice {inv.InvoiceNumber} balance of {money.Format(freshBalance)}");
                }
            }

            throw new InvalidOperationException("Concurrent payment conflict — please retry.");
        }

        // Record the number in the identifier registry (history + resolution). Inside the open
        // transaction so the registry row commits/rolls back with the payment.
        if (identifiers is not null)
            await identifiers.IssueAsync(BusinessEntityType.Payment, payment.Id, payment.PaymentNumber, cancellationToken);

        var appliedInvoiceIds = payment.Applications.Select(a => a.InvoiceId).Distinct().ToList();
        if (await InvoicedMilestonePayments.RefreshAsync(
                db, appliedInvoiceIds, payment.PaymentDate, payment.PaymentNumber, cancellationToken))
            await db.SaveChangesAsync(cancellationToken);

        // ── Order-to-cash completion. An order that is fully shipped with all its
        // (non-voided) invoices paid is done — advance it to Completed, the terminal
        // O2C state the prior pipeline never set (CreateShipment only reaches Shipped).
        // Runs after the invoice-Paid flush so the statuses checked here are committed.
        await PaymentApplicationRules.CompletePaidSalesOrdersAsync(db, completionCandidateSoIds, cancellationToken);

        // ── Inline cash-receipt posting (Phase-1 STAGE B, §7 matrix row "Payment
        // applied"). Runs AFTER the operational SaveChanges (within the same open
        // transaction) so the payment row — and its applications / invoice-status
        // changes — is flushed and the payment Id is assigned for the posting to
        // reference as its source. The engine posts on the SAME request-scoped
        // context and its SaveChanges joins this transaction; nothing commits until
        // the tx.CommitAsync below, so a posting failure rolls the payment back too
        // (the locked inline model — §2). No-op while CAP-ACCT-FULLGL is off; the
        // service self-gates, so the operational payment flow is unchanged while dark.
        if (cashPosting is not null)
        {
            var createdByUserId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : 0;

            await cashPosting.PostPaymentCreatedAsync(payment.Id, createdByUserId, cancellationToken);
        }

        // AUDIT-21-S1 (BLOCKER): enqueue a QBO payment sync row so AR cash receipts reach the
        // accounting provider — before this only MoveJobStage enqueued, leaving payments invisible to
        // sync. Inside the open transaction so the sync row commits/rolls back with the payment.
        if (accountingProviders is not null && syncQueue is not null
            && await accountingProviders.GetConnectedExternalProviderAsync(cancellationToken) is not null)
        {
            var accountingPayment = new AccountingPayment(
                ExternalId: customer.ExternalId ?? string.Empty,
                Amount: payment.Amount,
                Date: payment.PaymentDate,
                Method: payment.Method.ToString());
            await syncQueue.EnqueueAsync("Payment", payment.Id, "CreatePayment",
                JsonSerializer.Serialize(accountingPayment), cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer.Name,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt,
            payment.CurrencyId, currency.Code, payment.FxRate, FxMath.ToFunctional(payment.Amount, payment.FxRate));
    }

    // Uses a caller-supplied payment number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential number.
    private async Task<string> ResolvePaymentNumberAsync(CreatePaymentCommand request, CancellationToken ct)
    {
        var supplied = request.PaymentNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualPaymentNumbersAllowedAsync(ct))
        {
            if (await repo.PaymentNumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Payment number '{supplied}' is already in use.");
            return supplied;
        }

        return await repo.GenerateNextPaymentNumberAsync(ct);
    }

    private async Task<bool> ManualPaymentNumbersAllowedAsync(CancellationToken ct)
    {
        if (systemSettings is null) return false;
        var setting = await systemSettings.FindByKeyAsync(AllowManualPaymentNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
