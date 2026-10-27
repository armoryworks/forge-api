using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorCredits;

public record UpdateVendorCreditMemoCommand(int Id, UpdateVendorCreditMemoRequestModel Data) : IRequest<VendorCreditMemoDetailResponseModel>;

public class UpdateVendorCreditMemoValidator : AbstractValidator<UpdateVendorCreditMemoCommand>
{
    public UpdateVendorCreditMemoValidator()
    {
        RuleFor(x => x.Data.SourceType).IsInEnum();
        RuleFor(x => x.Data.Kind).IsInEnum();
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.VendorReference).MaximumLength(100);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.TaxAmount).GreaterThanOrEqualTo(0m).When(x => x.Data.TaxAmount.HasValue);
        RuleFor(x => x.Data.Lines).NotEmpty().WithMessage("At least one line is required");
        RuleForEach(x => x.Data.Lines).SetValidator(new VendorCreditMemoLineValidator());
    }
}

/// <summary>Edits a draft vendor memo's header and lines.</summary>
public class UpdateVendorCreditMemoHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateVendorCreditMemoCommand, VendorCreditMemoDetailResponseModel>
{
    public async Task<VendorCreditMemoDetailResponseModel> Handle(UpdateVendorCreditMemoCommand request, CancellationToken ct)
    {
        var memo = await VendorCreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor credit memo {request.Id} not found");
        if (memo.Status != VendorCreditMemoStatus.Draft)
            throw VendorCreditMemoRules.Failure("Id", VendorCreditMemoRules.NotDraftCode,
                $"Vendor memo {memo.MemoNumber} is {memo.Status}; only a draft can be edited.");

        var data = request.Data;
        memo.Kind = data.Kind;
        memo.SourceType = data.SourceType;
        memo.Reason = data.Reason.Trim();
        memo.VendorReference = string.IsNullOrWhiteSpace(data.VendorReference) ? null : data.VendorReference.Trim();
        memo.MemoDate = data.MemoDate;
        memo.Notes = data.Notes;
        if (data.TaxAmount is decimal tax)
            memo.TaxAmount = tax;

        db.VendorCreditMemoLines.RemoveRange(memo.Lines);
        await VendorCreditMemoRules.SetLinesAsync(db, memo, data.Lines, ct);

        db.LogActivityAt("updated", $"Updated draft vendor memo {memo.MemoNumber}", ("VendorCreditMemo", memo.Id));
        await db.SaveChangesAsync(ct);
        return await mediator.Send(new GetVendorCreditMemoByIdQuery(memo.Id), ct);
    }
}
