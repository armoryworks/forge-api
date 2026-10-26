using System.Globalization;

using FluentValidation;
using MediatR;

using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record WriteOffInvoiceCommand(int InvoiceId, decimal? Amount, string Reason) : IRequest<CreditMemoDetailResponseModel>;

public class WriteOffInvoiceValidator : AbstractValidator<WriteOffInvoiceCommand>
{
    public WriteOffInvoiceValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0m).MoneyScale().When(x => x.Amount.HasValue);
    }
}

/// <summary>
/// Writes off an invoice balance as bad debt in one transaction: creates a WriteOff credit memo for the amount (the
/// whole balance due by default), issues it (Dr Bad Debt Expense / Cr AR) and applies it to the invoice.
/// </summary>
public class WriteOffInvoiceHandler(AppDbContext db, CreditMemoIssuer issuer, IClock clock, IMediator mediator)
    : IRequestHandler<WriteOffInvoiceCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(WriteOffInvoiceCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.CurrentTransaction is not null)
            await db.AcquireAdvisoryLockAsync("invoices.write-off", request.InvoiceId.ToString(CultureInfo.InvariantCulture), ct);

        var invoice = await CreditMemoRules.LoadInvoiceAsync(db, request.InvoiceId, ct)
            ?? throw new KeyNotFoundException($"Invoice {request.InvoiceId} not found");
        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue))
            throw CreditMemoRules.Failure("InvoiceId", CreditMemoRules.CreditMemoInvoiceCode,
                $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only an open invoice can be written off.");

        var balance = invoice.BalanceDue;
        var amount = request.Amount ?? balance;
        var money = await MoneyFormat.ForCurrencyIdAsync(db, invoice.CurrencyId, ct);
        if (amount <= 0m || amount > balance)
            throw CreditMemoRules.Failure("Amount", CreditMemoRules.WriteOffExceedsBalanceCode,
                $"The write-off must be more than zero and at most invoice {invoice.InvoiceNumber}'s balance of {money.Format(balance)}.");

        var memo = new CreditMemo
        {
            CreditMemoNumber = await CreditMemoRules.NextCreditMemoNumberAsync(db, ct),
            CustomerId = invoice.CustomerId,
            CurrencyId = invoice.CurrencyId,
            FxRate = invoice.FxRate,
            InvoiceId = invoice.Id,
            ReasonType = CreditMemoReasonType.WriteOff,
            Reason = request.Reason.Trim(),
            CreditDate = clock.UtcNow,
            TaxRate = 0m,
            CreatedByUserId = db.CurrentUserId,
            Status = CreditMemoStatus.Draft,
        };
        memo.Lines.Add(new CreditMemoLine
        {
            LineNumber = 1,
            LineType = CreditMemoLineType.WriteOff,
            Description = $"Balance of invoice {invoice.InvoiceNumber} written off",
            Quantity = 1m,
            UnitPrice = amount,
            TaxCode = InvoiceLine.NonTaxableCode,
        });
        db.CreditMemos.Add(memo);
        db.LogActivityAt("written-off", $"Wrote off {money.Format(amount)} of invoice {invoice.InvoiceNumber}: {memo.Reason}",
            ("Invoice", invoice.Id), ("Customer", invoice.CustomerId));
        await db.SaveChangesAsync(ct);

        await issuer.IssueAsync(memo, ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
