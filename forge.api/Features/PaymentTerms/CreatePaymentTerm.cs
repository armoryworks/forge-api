using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentTerms;

public record CreatePaymentTermCommand(PaymentTermRequestModel Request) : IRequest<PaymentTermResponseModel>;

public class CreatePaymentTermValidator : AbstractValidator<CreatePaymentTermCommand>
{
    public CreatePaymentTermValidator()
    {
        RuleFor(x => x.Request).SetValidator(new PaymentTermValidator());
    }
}

public class CreatePaymentTermHandler(AppDbContext db) : IRequestHandler<CreatePaymentTermCommand, PaymentTermResponseModel>
{
    public const string DuplicateCode = "payment-terms-duplicate";

    public async Task<PaymentTermResponseModel> Handle(CreatePaymentTermCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var code = request.Code.Trim();

        if (await db.PaymentTerms.IgnoreQueryFilters().AnyAsync(t => t.Code == code, cancellationToken))
            throw PaymentTermsResolver.Failure("Request.Code", DuplicateCode, $"Payment terms with code {code} already exist.");

        var terms = new PaymentTerm
        {
            Code = code,
            Name = request.Name.Trim(),
            NetDays = request.NetDays,
            DiscountPercent = request.DiscountPercent,
            DiscountDays = request.DiscountPercent > 0m ? request.DiscountDays : 0,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
        };
        db.PaymentTerms.Add(terms);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Created payment terms {terms.Name}", ("PaymentTerm", terms.Id));
        await db.SaveChangesAsync(cancellationToken);

        return PaymentTermMapping.ToResponse(terms);
    }
}
