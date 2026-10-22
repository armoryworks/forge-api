using FluentAssertions;

using Forge.Api.Features.Onboarding;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Onboarding;

public class OnboardingFieldRulesTests
{
    private static readonly SaveOnboardingDraftRequestModel EmptyDraft = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static OnboardingSubmitRequestModel CompleteSubmission() => new(
        "Pat", null, "Lee", null, new DateTimeOffset(1990, 1, 2, 0, 0, 0, TimeSpan.Zero), "123-45-6789", "pat@example.test", "555-0100",
        "1 Main St", null, "Springfield", "IL", "62701",
        "single", false, 2000m, 0m, 0m, 0m, false,
        "single", 1, 0m, false,
        "1", null, null, null, null, null, false, null, null, null, null, null, null,
        "A", "Passport", null, "US", null, 10, null, null, null, null, null, null, null, null, null, null,
        "Bank", "011000015", "000123456789", "checking", null,
        true, false);

    [Fact]
    public void Draft_AcceptsBlankFields()
    {
        new SaveOnboardingDraftValidator()
            .Validate(new SaveOnboardingDraftCommand(1, EmptyDraft))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(21, 0, 0)]
    [InlineData(0, -2, 0)]
    [InlineData(0, 0, 100)]
    [InlineData(0, 0, -1)]
    public void Draft_RejectsOutOfRangeCounts(int children, int others, int allowances)
    {
        var draft = EmptyDraft with { W4QualifyingChildren = children, W4OtherDependents = others, StateAllowances = allowances };
        new SaveOnboardingDraftValidator()
            .Validate(new SaveOnboardingDraftCommand(1, draft))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Draft_RejectsNegativeOrAbsurdMoney()
    {
        var validator = new SaveOnboardingDraftValidator();
        validator.Validate(new SaveOnboardingDraftCommand(1, EmptyDraft with { W4ExtraWithholding = -5m })).IsValid.Should().BeFalse();
        validator.Validate(new SaveOnboardingDraftCommand(1, EmptyDraft with { StateAdditionalWithholding = 5_000_000m })).IsValid.Should().BeFalse();
        validator.Validate(new SaveOnboardingDraftCommand(1, EmptyDraft with { W4OtherIncome = 1200.50m })).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Submit_AcceptsACompleteSubmission()
    {
        new SaveOnboardingDataValidator()
            .Validate(new SaveOnboardingDataCommand(1, "pat@example.test", "Pat Lee", CompleteSubmission()))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Submit_RejectsASubmissionWithEarlierStepsBlank()
    {
        var blank = CompleteSubmission() with { FirstName = "", Street1 = "", W4FilingStatus = "", I9CitizenshipStatus = "" };

        var result = new SubmitOnboardingValidator()
            .Validate(new SubmitOnboardingCommand(1, "pat@example.test", "Pat Lee", blank));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(new[]
        {
            "Model.FirstName", "Model.Street1", "Model.W4FilingStatus", "Model.I9CitizenshipStatus",
        });
    }

    [Fact]
    public void Submit_RejectsNegativeWithholding()
    {
        var model = CompleteSubmission() with { W4ExtraWithholding = -1m, StateAllowances = -3 };
        new SaveOnboardingDataValidator()
            .Validate(new SaveOnboardingDataCommand(1, "pat@example.test", "Pat Lee", model))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(52_000, true)]
    [InlineData(999_999.99, true)]
    [InlineData(1_000_000, false)]
    [InlineData(-1, false)]
    public void Submit_BoundsTheDependentsAmountByTheMoneyMaximumNotAPerChildFigure(double amount, bool valid)
    {
        var model = CompleteSubmission() with { W4ClaimDependentsAmount = (decimal)amount };
        new SaveOnboardingDataValidator()
            .Validate(new SaveOnboardingDataCommand(1, "pat@example.test", "Pat Lee", model))
            .IsValid.Should().Be(valid);
    }
}
