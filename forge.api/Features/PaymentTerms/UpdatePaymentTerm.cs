using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>
/// Edits payment terms. System terms take a new name only; their days, discount and active flag stay as seeded.
/// Documents already issued keep the due date and discount they were created with.
/// </summary>
public record UpdatePaymentTermCommand(int Id, PaymentTermRequestModel Request) : IRequest<PaymentTermResponseModel>;

public class UpdatePaymentTermValidator : AbstractValidator<UpdatePaymentTermCommand>
{
    public UpdatePaymentTermValidator()
    {
        RuleFor(x => x.Request).SetValidator(new PaymentTermValidator());
    }
}

public class UpdatePaymentTermHandler(AppDbContext db) : IRequestHandler<UpdatePaymentTermCommand, PaymentTermResponseModel>
{
    public const string SystemTermsCode = "payment-terms-system";

    public async Task<PaymentTermResponseModel> Handle(UpdatePaymentTermCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var terms = await db.PaymentTerms.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment terms {command.Id} not found");

        var code = request.Code.Trim();
        var discountDays = request.DiscountPercent > 0m ? request.DiscountDays : 0;
        if (terms.IsSystem
            && (code != terms.Code || request.NetDays != terms.NetDays || request.DiscountPercent != terms.DiscountPercent
                || discountDays != terms.DiscountDays || !request.IsActive))
            throw PaymentTermsResolver.Failure("Request", SystemTermsCode,
                $"{terms.Name} are system terms: only the name and sort order can change.");

        if (code != terms.Code
            && await db.PaymentTerms.IgnoreQueryFilters().AnyAsync(t => t.Code == code && t.Id != terms.Id, cancellationToken))
            throw PaymentTermsResolver.Failure("Request.Code", CreatePaymentTermHandler.DuplicateCode, $"Payment terms with code {code} already exist.");

        var changed = new List<string>();
        void Track(string field, bool differs) { if (differs) changed.Add(field); }
        Track("code", code != terms.Code);
        Track("name", request.Name.Trim() != terms.Name);
        Track("net days", request.NetDays != terms.NetDays);
        Track("discount percent", request.DiscountPercent != terms.DiscountPercent);
        Track("discount days", discountDays != terms.DiscountDays);
        Track("active", request.IsActive != terms.IsActive);
        Track("sort order", request.SortOrder != terms.SortOrder);

        terms.Code = code;
        terms.Name = request.Name.Trim();
        terms.NetDays = request.NetDays;
        terms.DiscountPercent = request.DiscountPercent;
        terms.DiscountDays = discountDays;
        terms.IsActive = request.IsActive;
        terms.SortOrder = request.SortOrder;

        if (changed.Count > 0)
            db.LogActivityAt("updated", $"Updated payment terms {terms.Name}: {string.Join(", ", changed)}", ("PaymentTerm", terms.Id));
        await db.SaveChangesAsync(cancellationToken);

        return PaymentTermMapping.ToResponse(terms);
    }
}
