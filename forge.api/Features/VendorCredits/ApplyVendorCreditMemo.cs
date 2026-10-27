using System.Globalization;

using FluentValidation;
using MediatR;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

public record ApplyVendorCreditMemoCommand(int Id, int VendorBillId, decimal Amount) : IRequest<VendorCreditMemoDetailResponseModel>;

public class ApplyVendorCreditMemoValidator : AbstractValidator<ApplyVendorCreditMemoCommand>
{
    public ApplyVendorCreditMemoValidator()
    {
        RuleFor(x => x.VendorBillId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0m).MoneyScale();
    }
}

/// <summary>
/// Applies part of an open vendor memo to an approved bill of the same vendor and currency: the bill's balance due
/// drops by the amount, through the same open-item mechanism as a payment, and the memo's open amount with it.
/// </summary>
public class ApplyVendorCreditMemoHandler(AppDbContext db, IVendorCreditMemoPostingService posting, IClock clock, IMediator mediator)
    : IRequestHandler<ApplyVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(ApplyVendorCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("vendor-credit-memos.apply", request.Id.ToString(CultureInfo.InvariantCulture), ct);

        var memo = await VendorCreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");
        var bill = await VendorCreditMemoRules.LoadBillAsync(db, request.VendorBillId, ct)
            ?? throw new KeyNotFoundException($"Vendor bill {request.VendorBillId} not found");

        var application = await VendorCreditMemoRules.ApplyAsync(db, memo, bill, request.Amount, clock.UtcNow, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("credit-applied",
            $"Applied {money.Format(request.Amount)} of vendor memo {memo.MemoNumber} to bill {bill.BillNumber}",
            ("VendorCreditMemo", memo.Id), ("VendorBill", bill.Id), ("Vendor", memo.VendorId));
        await db.SaveChangesAsync(ct);

        await posting.PostApplicationAsync(application.Id, VendorCreditMemoRules.UserId(db), ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}
