using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// Creates a <see cref="VendorPayment"/> applied to one or more vendor bills (AP counterpart of
/// CreatePayment). Creating the payment IS the cash-disbursement posting trigger: when CAP-ACCT-FULLGL is
/// enabled, it posts Dr AP / Cr Cash inline, in this command's transaction.
/// </summary>
public record CreateVendorPaymentCommand(
    int VendorId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreateVendorPaymentApplicationModel>? Applications) : IRequest<VendorPaymentListItemModel>;

public class CreateVendorPaymentValidator : AbstractValidator<CreateVendorPaymentCommand>
{
    public CreateVendorPaymentValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).NotEmpty()
            // Constrain to the PaymentMethod enum so an invalid string is a 400, not a 500 from the
            // handler's Enum.Parse (the parse would throw ArgumentException unmapped by the middleware).
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Method must be one of: Cash, Check, CreditCard, BankTransfer, Wire, Other");
        When(x => x.Applications != null && x.Applications.Count > 0, () =>
        {
            RuleFor(x => x.Applications!.Sum(a => a.Amount))
                .LessThanOrEqualTo(x => x.Amount)
                .WithMessage("Applied amounts cannot exceed payment amount");
            // A bill referenced twice would bypass the per-bill over-apply guard (EF returns the same
            // tracked entity, so each check sees the same pre-application balance) — reject duplicates.
            RuleFor(x => x.Applications!)
                .Must(a => a.Select(x => x.VendorBillId).Distinct().Count() == a.Count)
                .WithMessage("A bill may be referenced at most once per payment");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.VendorBillId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
            });
        });
    }
}

public class CreateVendorPaymentHandler(
    IVendorPaymentRepository repo,
    IVendorRepository vendorRepo,
    IVendorBillRepository billRepo,
    AppDbContext db,
    // Optional / null-default (mirrors CreatePayment): the production DI path supplies both; with
    // CAP-ACCT-FULLGL off the posting service no-ops anyway.
    IVendorPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<CreateVendorPaymentCommand, VendorPaymentListItemModel>
{
    public async Task<VendorPaymentListItemModel> Handle(CreateVendorPaymentCommand request, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepo.FindAsync(request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor {request.VendorId} not found");

        // One unit of work: the payment + its applications + the bill-status updates AND the inline
        // cash-disbursement posting commit (or roll back) together (§2). On Npgsql this is a real
        // transaction; on the in-memory test provider it's an ignored no-op.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var paymentNumber = await repo.GenerateNextVendorPaymentNumberAsync(cancellationToken);
        var method = Enum.Parse<PaymentMethod>(request.Method, true);

        var payment = new VendorPayment
        {
            PaymentNumber = paymentNumber,
            VendorId = request.VendorId,
            Method = method,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
        };

        decimal appliedTotal = 0;

        if (request.Applications != null)
        {
            foreach (var app in request.Applications)
            {
                var bill = await billRepo.FindWithDetailsAsync(app.VendorBillId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Vendor bill {app.VendorBillId} not found");

                // Sub-ledger integrity: the payment's vendor must own the bill (else vendor A's payable
                // would be relieved by vendor B's bill and neither reconciles to its document).
                if (bill.VendorId != request.VendorId)
                    throw new InvalidOperationException(
                        $"Vendor bill {bill.BillNumber} belongs to a different vendor");

                // Only a booked payable can be paid. A Draft bill has not posted its AP credit (posting
                // fires on approval), so paying it would Dr AP against a liability the GL never recorded;
                // Void/Paid are terminal. Restrict to Approved / PartiallyPaid.
                if (bill.Status is not (VendorBillStatus.Approved or VendorBillStatus.PartiallyPaid))
                    throw new InvalidOperationException(
                        $"Vendor bill {bill.BillNumber} is {bill.Status}; only Approved or PartiallyPaid bills can be paid");

                // Consume the canonical VendorBill.BalanceDue (single source of truth), mirroring F-027.
                var balanceDue = bill.BalanceDue;

                if (app.Amount > balanceDue)
                    throw new InvalidOperationException(
                        $"Application amount {app.Amount:C} exceeds bill {bill.BillNumber} balance of {balanceDue:C}");

                payment.Applications.Add(new VendorPaymentApplication
                {
                    VendorBillId = app.VendorBillId,
                    Amount = app.Amount,
                });
                appliedTotal += app.Amount;

                var newBalance = balanceDue - app.Amount;
                if (newBalance <= 0)
                    bill.Status = VendorBillStatus.Paid;
                else if (bill.Status is VendorBillStatus.Approved or VendorBillStatus.PartiallyPaid)
                    bill.Status = VendorBillStatus.PartiallyPaid;

                // F-026: force the concurrency token modified so a concurrent SaveChanges on the same bill
                // row collides and throws DbUpdateConcurrencyException.
                db.Entry(bill).Property(b => b.Version).IsModified = true;
            }
        }

        await repo.AddAsync(payment, cancellationToken);

        try
        {
            await repo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A concurrent payment committed to this bill between our read and write. Reload each
            // conflicted bill (incl. navigations for balance calc) and re-run the over-apply guard.
            foreach (var entry in ex.Entries.Where(e => e.Entity is VendorBill))
            {
                await entry.ReloadAsync(cancellationToken);
                var b = (VendorBill)entry.Entity;
                await db.Entry(b).Collection(x => x.Lines).LoadAsync(cancellationToken);
                await db.Entry(b).Collection(x => x.PaymentApplications).LoadAsync(cancellationToken);

                var freshBalance = b.BalanceDue;

                var matchingApp = request.Applications!.First(a => a.VendorBillId == b.Id);
                if (matchingApp.Amount > freshBalance)
                    throw new InvalidOperationException(
                        $"Application amount {matchingApp.Amount:C} exceeds bill {b.BillNumber} balance of {freshBalance:C}");
            }

            throw new InvalidOperationException("Concurrent payment conflict — please retry.");
        }

        // ── Inline cash-disbursement posting (STAGE B-equivalent). Runs AFTER the operational SaveChanges
        // (within the same open transaction) so the payment row is flushed and payment.Id assigned for the
        // posting to reference; nothing commits until tx.CommitAsync, so a posting failure rolls the
        // payment back too. No-op while CAP-ACCT-FULLGL is off.
        if (cashPosting is not null)
        {
            var createdByUserId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : 0;

            await cashPosting.PostVendorPaymentCreatedAsync(payment.Id, createdByUserId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new VendorPaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.VendorId, vendor.CompanyName,
            payment.Method.ToString(), payment.Amount, appliedTotal, payment.Amount - appliedTotal,
            payment.PaymentDate, payment.ReferenceNumber, payment.CreatedAt);
    }
}
