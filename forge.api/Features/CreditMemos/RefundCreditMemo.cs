using System.Globalization;

using FluentValidation;
using MediatR;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record RefundCreditMemoCommand(int Id, CustomerRefundRequestModel Data) : IRequest<CreditMemoDetailResponseModel>;

public class CustomerRefundRequestValidator : AbstractValidator<CustomerRefundRequestModel>
{
    public CustomerRefundRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).MoneyScale();
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.SettlementFxRate).GreaterThan(0m).When(x => x.SettlementFxRate.HasValue);
    }
}

public class RefundCreditMemoValidator : AbstractValidator<RefundCreditMemoCommand>
{
    public RefundCreditMemoValidator()
    {
        RuleFor(x => x.Data).SetValidator(new CustomerRefundRequestValidator());
    }
}

/// <summary>Pays back some or all of an issued credit memo's open amount to the customer.</summary>
public class RefundCreditMemoHandler(AppDbContext db, ICreditMemoPostingService posting, IMediator mediator, CreditMemoRefundSync sync)
    : IRequestHandler<RefundCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(RefundCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("credit-memos.apply", request.Id.ToString(CultureInfo.InvariantCulture), ct);

        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        if (memo.Status is not (CreditMemoStatus.Issued or CreditMemoStatus.PartiallyApplied))
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoNotOpenCode,
                $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only an issued memo with an open amount can be refunded.");

        if (memo.IsWriteOff)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.WriteOffRefundCode,
                $"Credit memo {memo.CreditMemoNumber} writes off bad debt; a write-off can't be refunded.");

        var data = request.Data;
        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        if (data.Amount > memo.OpenAmount)
            throw CreditMemoRules.Failure("Amount", CreditMemoRules.RefundExceedsOpenCode,
                $"{money.Format(data.Amount)} is more than the {money.Format(memo.OpenAmount)} left on credit memo {memo.CreditMemoNumber}.");

        var refund = new CustomerRefund
        {
            RefundNumber = await CreditMemoRules.NextRefundNumberAsync(db, ct),
            CustomerId = memo.CustomerId,
            CurrencyId = memo.CurrencyId,
            FxRate = data.SettlementFxRate ?? memo.FxRate,
            Amount = data.Amount,
            Method = data.Method,
            RefundDate = data.RefundDate,
            Reference = data.Reference,
            Notes = data.Notes,
            CreditMemoId = memo.Id,
            CreatedByUserId = db.CurrentUserId,
        };
        memo.Refunds.Add(refund);
        memo.RecomputeStatus();
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("refunded",
            $"Refunded {money.Format(data.Amount)} of credit memo {memo.CreditMemoNumber} ({refund.RefundNumber})",
            ("CreditMemo", memo.Id), ("Customer", memo.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.PostRefundAsync(refund.Id, CreditMemoRules.UserId(db), ct);
        await sync.EnqueueAsync(refund, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
