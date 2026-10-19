using FluentAssertions;
using Moq;

using Forge.Api.Features.Onboarding;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Onboarding;

public class DirectDepositCompletionTests
{
    private static readonly SaveOnboardingDraftRequestModel Empty = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private readonly Mock<IPiiProtector> _pii = new();

    public DirectDepositCompletionTests()
    {
        _pii.Setup(p => p.Unprotect(It.IsAny<string?>()))
            .Returns((string? v) => string.IsNullOrWhiteSpace(v) ? null : v["enc:".Length..]);
    }

    private bool IsComplete(EmployeeProfile profile, string? bank, string? routing, string? account)
        => OnboardingPiiMerge.HasCompleteDirectDeposit(profile, _pii.Object, bank, routing, account);

    [Fact]
    public void BankAndValidRoutingAndAccount_IsComplete()
        => IsComplete(new EmployeeProfile(), "First Bank", "111000025", "123456789").Should().BeTrue();

    [Fact]
    public void MissingAccountNumber_IsNotComplete()
        => IsComplete(new EmployeeProfile(), "First Bank", "111000025", "").Should().BeFalse();

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345")]
    [InlineData("abcdefghi")]
    public void InvalidRoutingNumber_IsNotComplete(string routing)
        => IsComplete(new EmployeeProfile(), "First Bank", routing, "123456789").Should().BeFalse();

    [Fact]
    public void MissingBankName_IsNotComplete()
        => IsComplete(new EmployeeProfile(), null, "111000025", "123456789").Should().BeFalse();

    [Fact]
    public void BlankFieldsFallBackToStoredDraft()
    {
        var profile = new EmployeeProfile
        {
            BankName = "First Bank",
            BankRoutingProtected = "enc:111000025",
            BankAccountProtected = "enc:123456789",
        };

        IsComplete(profile, "", "", "").Should().BeTrue();
    }

    [Fact]
    public void StoredDraftWithoutAccount_IsNotComplete()
    {
        var profile = new EmployeeProfile { BankName = "First Bank", BankRoutingProtected = "enc:111000025" };

        IsComplete(profile, "", "", "").Should().BeFalse();
    }

    [Theory]
    [InlineData("123456789", false)]
    [InlineData("111000025", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void DraftValidator_RejectsRoutingNumberThatFailsChecksum(string? routing, bool valid)
    {
        var model = Empty with { RoutingNumber = routing };

        new SaveOnboardingDraftValidator()
            .Validate(new SaveOnboardingDraftCommand(1, model))
            .IsValid.Should().Be(valid);
    }
}
