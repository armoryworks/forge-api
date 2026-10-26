using FluentValidation;
using MediatR;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

public record VoidVendorCreditMemoCommand(int Id, string Reason) : IRequest<VendorCreditMemoDetailResponseModel>;

public class VoidVendorCreditMemoValidator : AbstractValidator<VoidVendorCreditMemoCommand>
{
    public VoidVendorCreditMemoValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Voids an issued vendor memo and reverses its posting. Refused while any of it is applied to a bill
/// (<c>vendor-credit-applied</c>): unapply it first. A voided return credit puts its return back to Shipped.
/// </summary>
public class VoidVendorCreditMemoHandler(AppDbContext db, IVendorCreditMemoPostingService posting, IClock clock, IMediator mediator)
    : IRequestHandler<VoidVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(VoidVendorCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await VendorCreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");
        if (memo.Status is VendorCreditMemoStatus.Draft or VendorCreditMemoStatus.Voided)
            throw VendorCreditMemoRules.Failure("Id", VendorCreditMemoRules.NotOpenCode,
                $"Vendor memo {memo.MemoNumber} is {memo.Status}; only an issued memo can be voided.");
        if (memo.AmountApplied > 0m)
            throw VendorCreditMemoRules.Failure("Id", VendorCreditMemoRules.AppliedCode,
                $"Vendor memo {memo.MemoNumber} is applied to bills. Unapply it first.");

        var now = clock.UtcNow;
        memo.Status = VendorCreditMemoStatus.Voided;
        memo.VoidedAt = now;
        memo.VoidReason = request.Reason.Trim();

        if (memo.VendorReturnId is int returnId)
        {
            var rtv = await db.VendorReturns.FindAsync([returnId], ct);
            if (rtv is { Status: VendorReturnStatus.Credited or VendorReturnStatus.Closed })
                rtv.Status = VendorReturnStatus.Shipped;
        }

        db.LogActivityAt("voided", $"Voided vendor memo {memo.MemoNumber}: {memo.VoidReason}",
            ("VendorCreditMemo", memo.Id), ("Vendor", memo.VendorId));
        await db.SaveChangesAsync(ct);

        await posting.ReverseIssueAsync(memo.Id, await VendorCreditMemoRules.ShopTodayAsync(db, now, ct), VendorCreditMemoRules.UserId(db), ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}
