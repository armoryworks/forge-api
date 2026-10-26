using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record CreateCreditMemoCommand(CreateCreditMemoRequestModel Data, int? CustomerReturnId = null) : IRequest<CreditMemoDetailResponseModel>;

public class CreateCreditMemoValidator : AbstractValidator<CreateCreditMemoCommand>
{
    public CreateCreditMemoValidator()
    {
        RuleFor(x => x.Data.CustomerId).GreaterThan(0);
        RuleFor(x => x.Data.ReasonType).IsInEnum();
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.TaxRate).GreaterThanOrEqualTo(0m).LessThan(1m).When(x => x.Data.TaxRate.HasValue);
        RuleFor(x => x.Data.FxRate).GreaterThan(0m).When(x => x.Data.FxRate.HasValue);
        RuleFor(x => x.Data.Lines).NotEmpty().WithMessage("At least one line is required");
        RuleForEach(x => x.Data.Lines).SetValidator(new CreditMemoLineValidator());
    }
}

public class CreditMemoLineValidator : AbstractValidator<CreditMemoLineRequestModel>
{
    public CreditMemoLineValidator()
    {
        RuleFor(l => l.LineType).IsInEnum();
        RuleFor(l => l.Description).MaximumLength(500);
        RuleFor(l => l.Quantity).GreaterThan(0m);
        RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
        RuleFor(l => l.PartId).Null().When(l => l.LineType is CreditMemoLineType.Freight or CreditMemoLineType.WriteOff)
            .WithMessage("A freight or write-off line can't name a part.");
    }
}

/// <summary>
/// Creates a draft credit memo. Linked to an invoice, the memo takes the invoice's customer, currency, booking rate
/// and tax rate (the tax rate can be overridden); standalone, it takes the requested currency (rate on file when none
/// is sent) and tax rate.
/// </summary>
public class CreateCreditMemoHandler(AppDbContext db, IClock clock, IMediator mediator)
    : IRequestHandler<CreateCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(CreateCreditMemoCommand request, CancellationToken ct)
    {
        var data = request.Data;
        CreditMemoRules.EnsureNoWriteOff(data.ReasonType, data.Lines);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == data.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Customer {data.CustomerId} not found");

        var creditDate = data.CreditDate ?? clock.UtcNow;
        var memo = new CreditMemo
        {
            CustomerId = customer.Id,
            ReasonType = data.ReasonType,
            Reason = data.Reason.Trim(),
            Notes = data.Notes,
            CreditDate = creditDate,
            CreatedByUserId = db.CurrentUserId,
            CustomerReturnId = request.CustomerReturnId,
            Status = CreditMemoStatus.Draft,
        };

        if (data.InvoiceId is int invoiceId)
        {
            var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                ?? throw new KeyNotFoundException($"Invoice {invoiceId} not found");
            if (invoice.CustomerId != customer.Id)
                throw CreditMemoRules.Failure("InvoiceId", CreditMemoRules.CreditMemoInvoiceCode,
                    $"Invoice {invoice.InvoiceNumber} belongs to a different customer.");
            if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Voided)
                throw CreditMemoRules.Failure("InvoiceId", CreditMemoRules.CreditMemoInvoiceCode,
                    $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only a sent invoice can be credited.");
            memo.InvoiceId = invoice.Id;
            memo.CurrencyId = invoice.CurrencyId;
            memo.FxRate = invoice.FxRate;
            memo.TaxRate = data.TaxRate ?? invoice.TaxRate;
            memo.SalesTaxRateId = invoice.SalesTaxRateId;
        }
        else
        {
            var currency = await new DocumentCurrencyResolver(db).ResolveAsync(data.CurrencyId, data.FxRate, null, creditDate, ct);
            memo.CurrencyId = currency.CurrencyId;
            memo.FxRate = currency.FxRate;
            memo.TaxRate = data.TaxRate ?? 0m;
        }

        await CreditMemoRules.SetLinesAsync(db, memo, data.Lines, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        memo.CreditMemoNumber = await CreditMemoRules.NextCreditMemoNumberAsync(db, ct);
        db.CreditMemos.Add(memo);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created", $"Created credit memo {memo.CreditMemoNumber} for {customer.Name}",
            ("CreditMemo", memo.Id), ("Customer", customer.Id));
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
