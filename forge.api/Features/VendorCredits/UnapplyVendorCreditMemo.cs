using MediatR;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

public record UnapplyVendorCreditMemoCommand(int Id, int ApplicationId) : IRequest<VendorCreditMemoDetailResponseModel>;

/// <summary>Takes a vendor memo application back off its bill: the bill's balance and the memo's open amount return.</summary>
public class UnapplyVendorCreditMemoHandler(AppDbContext db, IVendorCreditMemoPostingService posting, IClock clock, IMediator mediator)
    : IRequestHandler<UnapplyVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(UnapplyVendorCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await VendorCreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");
        var application = memo.Applications.FirstOrDefault(a => a.Id == request.ApplicationId && a.ReversedAt == null)
            ?? throw new KeyNotFoundException($"Application {request.ApplicationId} not found on vendor memo {memo.MemoNumber}");

        await VendorCreditMemoApplicationReversal.ReverseAsync(db, posting, memo, application, clock.UtcNow, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}

/// <summary>Unapplies one vendor memo application: stamps it reversed, restores the bill and memo, reverses the posting.</summary>
internal static class VendorCreditMemoApplicationReversal
{
    public static async Task ReverseAsync(
        AppDbContext db, IVendorCreditMemoPostingService posting, Forge.Core.Entities.VendorCreditMemo memo,
        Forge.Core.Entities.VendorCreditMemoApplication application, DateTimeOffset now, CancellationToken ct)
    {
        var bill = await VendorCreditMemoRules.LoadBillAsync(db, application.VendorBillId, ct)
            ?? throw new KeyNotFoundException($"Vendor bill {application.VendorBillId} not found");

        application.ReversedAt = now;
        application.ReversedByUserId = db.CurrentUserId;
        VendorCreditMemoRules.PromoteBill(bill);
        memo.RecomputeStatus();
        db.Entry(bill).Property(b => b.Version).IsModified = true;

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("credit-unapplied",
            $"Unapplied {money.Format(application.Amount)} of vendor memo {memo.MemoNumber} from bill {bill.BillNumber}",
            ("VendorCreditMemo", memo.Id), ("VendorBill", bill.Id));
        await db.SaveChangesAsync(ct);

        await posting.ReverseApplicationAsync(application.Id, await VendorCreditMemoRules.ShopTodayAsync(db, now, ct),
            VendorCreditMemoRules.UserId(db), ct);
    }
}
