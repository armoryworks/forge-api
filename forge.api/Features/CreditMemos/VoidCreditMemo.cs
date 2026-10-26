using FluentValidation;
using MediatR;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record VoidCreditMemoCommand(int Id, string Reason) : IRequest<CreditMemoDetailResponseModel>;

public class VoidCreditMemoValidator : AbstractValidator<VoidCreditMemoCommand>
{
    public VoidCreditMemoValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Voids an issued credit memo and reverses its posting. Refused while any of it is applied to an invoice or refunded
/// (<c>credit-memo-applied</c>): unapply or void those first.
/// </summary>
public class VoidCreditMemoHandler(AppDbContext db, ICreditMemoPostingService posting, IClock clock, IMediator mediator)
    : IRequestHandler<VoidCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(VoidCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        if (memo.Status is CreditMemoStatus.Draft or CreditMemoStatus.Voided)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoNotOpenCode,
                $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only an issued memo can be voided.");
        if (memo.AmountApplied > 0m || memo.AmountRefunded > 0m)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoAppliedCode,
                $"Credit memo {memo.CreditMemoNumber} is applied to invoices or refunded. Unapply it and void its refunds first.");

        var now = clock.UtcNow;
        memo.Status = CreditMemoStatus.Voided;
        memo.VoidedAt = now;
        memo.VoidReason = request.Reason.Trim();

        db.LogActivityAt("voided", $"Voided credit memo {memo.CreditMemoNumber}: {memo.VoidReason}",
            ("CreditMemo", memo.Id), ("Customer", memo.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.ReverseIssueAsync(memo.Id, await CreditMemoRules.ShopTodayAsync(db, now, ct), CreditMemoRules.UserId(db), ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
