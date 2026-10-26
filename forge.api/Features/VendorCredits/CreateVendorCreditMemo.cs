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

namespace Forge.Api.Features.VendorCredits;

public record CreateVendorCreditMemoCommand(CreateVendorCreditMemoRequestModel Data, int? VendorReturnId = null)
    : IRequest<VendorCreditMemoDetailResponseModel>;

public class CreateVendorCreditMemoValidator : AbstractValidator<CreateVendorCreditMemoCommand>
{
    public CreateVendorCreditMemoValidator()
    {
        RuleFor(x => x.Data.VendorId).GreaterThan(0);
        RuleFor(x => x.Data.SourceType).IsInEnum();
        RuleFor(x => x.Data.Kind).IsInEnum();
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.VendorReference).MaximumLength(100);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.TaxAmount).GreaterThanOrEqualTo(0m).When(x => x.Data.TaxAmount.HasValue);
        RuleFor(x => x.Data.FxRate).GreaterThan(0m).When(x => x.Data.FxRate.HasValue);
        RuleFor(x => x.Data.Lines).NotEmpty().WithMessage("At least one line is required");
        RuleForEach(x => x.Data.Lines).SetValidator(new VendorCreditMemoLineValidator());
    }
}

public class VendorCreditMemoLineValidator : AbstractValidator<VendorCreditMemoLineRequestModel>
{
    public VendorCreditMemoLineValidator()
    {
        RuleFor(l => l.Description).MaximumLength(500);
        RuleFor(l => l.AccountDeterminationKey).MaximumLength(50);
        RuleFor(l => l.Quantity).GreaterThan(0m);
        RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
    }
}

/// <summary>
/// Creates a draft vendor credit or debit memo. Linked to a bill, the memo takes the bill's vendor, currency and booking
/// rate; standalone, it takes the requested currency (rate on file when none is sent), defaulting to the vendor's.
/// </summary>
public class CreateVendorCreditMemoHandler(AppDbContext db, IClock clock, IMediator mediator)
    : IRequestHandler<CreateVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(CreateVendorCreditMemoCommand request, CancellationToken ct)
    {
        var data = request.Data;
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == data.VendorId, ct)
            ?? throw new KeyNotFoundException($"Vendor {data.VendorId} not found");

        var memoDate = data.MemoDate ?? clock.UtcNow;
        var memo = new VendorCreditMemo
        {
            VendorId = vendor.Id,
            Kind = data.Kind,
            SourceType = data.SourceType,
            Reason = data.Reason.Trim(),
            VendorReference = string.IsNullOrWhiteSpace(data.VendorReference) ? null : data.VendorReference.Trim(),
            Notes = data.Notes,
            MemoDate = memoDate,
            TaxAmount = data.TaxAmount ?? 0m,
            CreatedByUserId = db.CurrentUserId,
            VendorReturnId = request.VendorReturnId,
            Status = VendorCreditMemoStatus.Draft,
        };

        if (data.VendorBillId is int billId)
        {
            var bill = await db.VendorBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct)
                ?? throw new KeyNotFoundException($"Vendor bill {billId} not found");
            if (bill.VendorId != vendor.Id)
                throw VendorCreditMemoRules.Failure("VendorBillId", VendorCreditMemoRules.BillCode,
                    $"Bill {bill.BillNumber} belongs to a different vendor.");
            if (bill.Status is VendorBillStatus.Draft or VendorBillStatus.Void)
                throw VendorCreditMemoRules.Failure("VendorBillId", VendorCreditMemoRules.BillCode,
                    $"Bill {bill.BillNumber} is {bill.Status}; only an approved bill can be credited.");
            memo.VendorBillId = bill.Id;
            memo.CurrencyId = bill.CurrencyId;
            memo.FxRate = bill.FxRate;
        }
        else if (request.VendorReturnId is int returnId)
        {
            var rtv = await db.VendorReturns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == returnId, ct)
                ?? throw new KeyNotFoundException($"Vendor return {returnId} not found");
            memo.CurrencyId = rtv.CurrencyId;
            memo.FxRate = rtv.FxRate;
        }
        else
        {
            var currency = await new DocumentCurrencyResolver(db).ResolveAsync(data.CurrencyId, data.FxRate, vendor.CurrencyId, memoDate, ct);
            memo.CurrencyId = currency.CurrencyId;
            memo.FxRate = currency.FxRate;
        }

        await VendorCreditMemoRules.SetLinesAsync(db, memo, data.Lines, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        memo.MemoNumber = await VendorCreditMemoRules.NextMemoNumberAsync(db, ct);
        db.VendorCreditMemos.Add(memo);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt("created",
            memo.Kind == VendorCreditMemoKind.DebitMemo
                ? $"Created debit memo {memo.MemoNumber} for {vendor.CompanyName}"
                : $"Created vendor credit memo {memo.MemoNumber} for {vendor.CompanyName}",
            ("VendorCreditMemo", memo.Id), ("Vendor", vendor.Id));
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}
