using System.Globalization;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Payments;
using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record ApplyCreditMemoCommand(int Id, int InvoiceId, decimal Amount) : IRequest<CreditMemoDetailResponseModel>;

public class ApplyCreditMemoValidator : AbstractValidator<ApplyCreditMemoCommand>
{
    public ApplyCreditMemoValidator()
    {
        RuleFor(x => x.InvoiceId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0m).MoneyScale();
    }
}

/// <summary>
/// Applies part of an issued credit memo to an open invoice of the same customer and currency: the invoice's balance
/// due drops by the amount and the memo's open amount with it.
/// </summary>
public class ApplyCreditMemoHandler(AppDbContext db, ICreditMemoPostingService posting, IClock clock, IMediator mediator,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<ApplyCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(ApplyCreditMemoCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.AcquireAdvisoryLockAsync("credit-memos.apply", request.Id.ToString(CultureInfo.InvariantCulture), ct);

        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        CreditMemoRules.EnsureWriteOffApprover(memo, httpContextAccessor?.HttpContext?.User);
        var invoice = await CreditMemoRules.LoadInvoiceAsync(db, request.InvoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice {request.InvoiceId} not found");

        var application = await CreditMemoRules.ApplyAsync(db, memo, invoice, request.Amount, clock.UtcNow, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, memo.CurrencyId, ct);
        db.LogActivityAt("credit-applied",
            $"Applied {money.Format(request.Amount)} of credit memo {memo.CreditMemoNumber} to invoice {invoice.InvoiceNumber}",
            ("CreditMemo", memo.Id), ("Invoice", invoice.Id), ("Customer", memo.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.PostApplicationAsync(application.Id, CreditMemoRules.UserId(db), ct);
        if (invoice.Status == InvoiceStatus.Paid && invoice.SalesOrderId is int soId)
            await PaymentApplicationRules.CompletePaidSalesOrdersAsync(db, [soId], ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
