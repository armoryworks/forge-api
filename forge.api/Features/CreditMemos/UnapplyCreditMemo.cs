using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record UnapplyCreditMemoCommand(int Id, int ApplicationId) : IRequest<CreditMemoDetailResponseModel>;

/// <summary>Takes a credit memo application back off its invoice: the invoice's balance and the memo's open amount return.</summary>
public class UnapplyCreditMemoHandler(AppDbContext db, ICreditMemoPostingService posting, IClock clock, IMediator mediator,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<UnapplyCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(UnapplyCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        CreditMemoRules.EnsureWriteOffApprover(memo, httpContextAccessor?.HttpContext?.User);
        var application = memo.Applications.FirstOrDefault(a => a.Id == request.ApplicationId && a.ReversedAt == null)
            ?? throw new KeyNotFoundException($"Application {request.ApplicationId} not found on credit memo {memo.CreditMemoNumber}");
        var invoice = await CreditMemoRules.LoadInvoiceAsync(db, application.InvoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice {application.InvoiceId} not found");

        var now = clock.UtcNow;
        application.ReversedAt = now;
        application.ReversedByUserId = db.CurrentUserId;
        CreditMemoRules.PromoteInvoice(invoice);
        memo.RecomputeStatus();
        db.Entry(invoice).Property(i => i.Version).IsModified = true;

        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("credit-unapplied",
            $"Unapplied {money.Format(application.Amount)} of credit memo {memo.CreditMemoNumber} from invoice {invoice.InvoiceNumber}",
            ("CreditMemo", memo.Id), ("Invoice", invoice.Id));
        await db.SaveChangesAsync(ct);

        await posting.ReverseApplicationAsync(application.Id, await CreditMemoRules.ShopTodayAsync(db, now, ct), CreditMemoRules.UserId(db), ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
