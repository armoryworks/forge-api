using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record UpdateCreditMemoCommand(int Id, UpdateCreditMemoRequestModel Data) : IRequest<CreditMemoDetailResponseModel>;

public class UpdateCreditMemoValidator : AbstractValidator<UpdateCreditMemoCommand>
{
    public UpdateCreditMemoValidator()
    {
        RuleFor(x => x.Data.ReasonType).IsInEnum();
        RuleFor(x => x.Data.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Data.Notes).MaximumLength(2000);
        RuleFor(x => x.Data.TaxRate).GreaterThanOrEqualTo(0m).LessThan(1m).When(x => x.Data.TaxRate.HasValue);
        RuleFor(x => x.Data.Lines).NotEmpty().WithMessage("At least one line is required");
        RuleForEach(x => x.Data.Lines).SetValidator(new CreditMemoLineValidator());
    }
}

/// <summary>Edits a draft credit memo's reason, date, tax rate, notes and lines.</summary>
public class UpdateCreditMemoHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateCreditMemoCommand, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(UpdateCreditMemoCommand request, CancellationToken ct)
    {
        var memo = await CreditMemoRules.LoadMemoAsync(db, request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");
        if (memo.Status != CreditMemoStatus.Draft)
            throw CreditMemoRules.Failure("Id", CreditMemoRules.CreditMemoNotDraftCode,
                $"Credit memo {memo.CreditMemoNumber} is {memo.Status}; only a draft can be edited.");

        var data = request.Data;
        CreditMemoRules.EnsureNoWriteOff(data.ReasonType, data.Lines);
        memo.ReasonType = data.ReasonType;
        memo.Reason = data.Reason.Trim();
        memo.CreditDate = data.CreditDate;
        memo.Notes = data.Notes;
        if (data.TaxRate is decimal rate)
            memo.TaxRate = rate;

        db.CreditMemoLines.RemoveRange(memo.Lines);
        await CreditMemoRules.SetLinesAsync(db, memo, data.Lines, ct);

        db.LogActivityAt("updated", $"Updated draft credit memo {memo.CreditMemoNumber}", ("CreditMemo", memo.Id));
        await db.SaveChangesAsync(ct);
        return await mediator.Send(new GetCreditMemoByIdQuery(memo.Id), ct);
    }
}
