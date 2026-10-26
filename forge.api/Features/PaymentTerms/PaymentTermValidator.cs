using FluentValidation;

using Forge.Core.Models;

namespace Forge.Api.Features.PaymentTerms;

/// <summary>Shape rules shared by create and update: net and discount days, and a discount that ends before the net date.</summary>
public class PaymentTermValidator : AbstractValidator<PaymentTermRequestModel>
{
    public const string DiscountDaysCode = "payment-terms-discount-days";

    public PaymentTermValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NetDays).InclusiveBetween(0, 365);
        RuleFor(x => x.DiscountPercent).GreaterThanOrEqualTo(0m).LessThan(100m).PrecisionScale(7, 4, true);
        RuleFor(x => x.DiscountDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DiscountDays)
            .LessThan(x => x.NetDays)
            .When(x => x.DiscountPercent > 0m)
            .WithErrorCode(DiscountDaysCode)
            .WithMessage("The discount days must be fewer than the net days.");
        RuleFor(x => x.DiscountDays)
            .GreaterThan(0)
            .When(x => x.DiscountPercent > 0m)
            .WithErrorCode(DiscountDaysCode)
            .WithMessage("A discount needs at least one discount day.");
    }
}
