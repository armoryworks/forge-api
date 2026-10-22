using FluentValidation;

using Forge.Core.Models;

namespace Forge.Api.Features.Onboarding;

public static class OnboardingFieldRules
{
    public const int MaxDependents = 20;
    public const int MaxStateAllowances = 99;
    public const decimal MaxMoney = 999_999.99m;
}

public class OnboardingSubmitModelValidator : AbstractValidator<OnboardingSubmitRequestModel>
{
    public OnboardingSubmitModelValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.Street1).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.AddressState).NotEmpty();
        RuleFor(x => x.ZipCode).NotEmpty();
        RuleFor(x => x.W4FilingStatus).NotEmpty();
        RuleFor(x => x.I9CitizenshipStatus).NotEmpty();

        RuleFor(x => x.W4ClaimDependentsAmount).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney);
        RuleFor(x => x.W4OtherIncome).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney);
        RuleFor(x => x.W4Deductions).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney);
        RuleFor(x => x.W4ExtraWithholding).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney);
        RuleFor(x => x.StateAllowances!.Value).InclusiveBetween(0, OnboardingFieldRules.MaxStateAllowances)
            .When(x => x.StateAllowances.HasValue).OverridePropertyName(nameof(OnboardingSubmitRequestModel.StateAllowances));
        RuleFor(x => x.StateAdditionalWithholding!.Value).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney)
            .When(x => x.StateAdditionalWithholding.HasValue).OverridePropertyName(nameof(OnboardingSubmitRequestModel.StateAdditionalWithholding));
    }
}

public class OnboardingDraftModelValidator : AbstractValidator<SaveOnboardingDraftRequestModel>
{
    public OnboardingDraftModelValidator()
    {
        RuleFor(x => x.W4QualifyingChildren!.Value).InclusiveBetween(0, OnboardingFieldRules.MaxDependents)
            .When(x => x.W4QualifyingChildren.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.W4QualifyingChildren));
        RuleFor(x => x.W4OtherDependents!.Value).InclusiveBetween(0, OnboardingFieldRules.MaxDependents)
            .When(x => x.W4OtherDependents.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.W4OtherDependents));
        RuleFor(x => x.W4OtherIncome!.Value).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney)
            .When(x => x.W4OtherIncome.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.W4OtherIncome));
        RuleFor(x => x.W4Deductions!.Value).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney)
            .When(x => x.W4Deductions.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.W4Deductions));
        RuleFor(x => x.W4ExtraWithholding!.Value).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney)
            .When(x => x.W4ExtraWithholding.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.W4ExtraWithholding));
        RuleFor(x => x.StateAllowances!.Value).InclusiveBetween(0, OnboardingFieldRules.MaxStateAllowances)
            .When(x => x.StateAllowances.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.StateAllowances));
        RuleFor(x => x.StateAdditionalWithholding!.Value).InclusiveBetween(0m, OnboardingFieldRules.MaxMoney)
            .When(x => x.StateAdditionalWithholding.HasValue).OverridePropertyName(nameof(SaveOnboardingDraftRequestModel.StateAdditionalWithholding));
    }
}
