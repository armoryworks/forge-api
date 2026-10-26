using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record RefundPaymentCommand(int PaymentId, CustomerRefundRequestModel Data) : IRequest<CustomerRefundModel>;

public class RefundPaymentValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentValidator()
    {
        RuleFor(x => x.Data).SetValidator(new CustomerRefundRequestValidator());
    }
}

/// <summary>Pays back unapplied cash on a customer payment (an overpayment or a deposit no longer needed).</summary>
public class RefundPaymentHandler(AppDbContext db, ICreditMemoPostingService posting, CreditMemoRefundSync sync)
    : IRequestHandler<RefundPaymentCommand, CustomerRefundModel>
{
    public async Task<CustomerRefundModel> Handle(RefundPaymentCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT id FROM payments WHERE id = {0} FOR UPDATE", [request.PaymentId], ct);

        var payment = await db.Payments
            .Include(p => p.Applications)
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct)
            ?? throw new KeyNotFoundException($"Payment {request.PaymentId} not found");

        var data = request.Data;
        var currencyId = await new DocumentCurrencyResolver(db).EffectiveCurrencyIdAsync(payment.CurrencyId, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, currencyId, ct);
        if (data.Amount > payment.UnappliedAmount)
            throw CreditMemoRules.Failure("Amount", CreditMemoRules.RefundExceedsOpenCode,
                $"{money.Format(data.Amount)} is more than payment {payment.PaymentNumber}'s unapplied {money.Format(payment.UnappliedAmount)}.");

        var refund = new CustomerRefund
        {
            RefundNumber = await CreditMemoRules.NextRefundNumberAsync(db, ct),
            CustomerId = payment.CustomerId,
            CurrencyId = currencyId,
            FxRate = data.SettlementFxRate ?? payment.FxRate,
            Amount = data.Amount,
            Method = data.Method,
            RefundDate = data.RefundDate,
            Reference = data.Reference,
            Notes = data.Notes,
            PaymentId = payment.Id,
            CreatedByUserId = db.CurrentUserId,
        };
        payment.Refunds.Add(refund);
        db.Entry(payment).Property(p => p.Version).IsModified = true;
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("refunded",
            $"Refunded {money.Format(data.Amount)} of unapplied cash on payment {payment.PaymentNumber} ({refund.RefundNumber})",
            ("Payment", payment.Id), ("Customer", payment.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.PostRefundAsync(refund.Id, CreditMemoRules.UserId(db), ct);
        await sync.EnqueueAsync(refund, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);

        var code = await db.Currencies.AsNoTracking().Where(c => c.Id == currencyId).Select(c => c.Code).FirstOrDefaultAsync(ct);
        return new CustomerRefundModel(
            refund.Id, refund.RefundNumber, refund.CustomerId, refund.Amount, refund.Method.ToString(), refund.RefundDate,
            refund.Reference, refund.Status.ToString(), null, null, payment.Id, payment.PaymentNumber, code, refund.FxRate);
    }
}
