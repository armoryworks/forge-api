using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums.Accounting;

namespace Forge.Tests.Accounting;

public class GlAccountValidatorTests
{
    private static CreateGlAccountCommand Create(string number, string name)
        => new(1, number, name, AccountType.Expense, NormalBalance.Debit, null, false, false, null, null);

    private static UpdateGlAccountCommand Update(string? number, string name)
        => new(1, name, null, false, false, null, null, true, number, null, null);

    [Theory]
    [InlineData("60100")]
    [InlineData("6010-02")]
    [InlineData("1.100")]
    [InlineData(" 60100 ")]
    public void Create_AcceptsAccountNumbers(string number)
        => new CreateGlAccountValidator().Validate(Create(number, "Supplies")).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("60 100")]
    [InlineData("-60100")]
    [InlineData("60100/")]
    [InlineData("   ")]
    public void Create_RejectsMalformedAccountNumbers(string number)
        => new CreateGlAccountValidator().Validate(Create(number, "Supplies")).IsValid.Should().BeFalse();

    [Fact]
    public void Create_RejectsWhitespaceOnlyName()
        => new CreateGlAccountValidator().Validate(Create("60100", "   ")).IsValid.Should().BeFalse();

    [Fact]
    public void Update_ValidatesNumberOnlyWhenSent()
    {
        var validator = new UpdateGlAccountValidator();

        validator.Validate(Update(null, "Supplies")).IsValid.Should().BeTrue();
        validator.Validate(Update("60100", "Supplies")).IsValid.Should().BeTrue();
        validator.Validate(Update("60 100", "Supplies")).IsValid.Should().BeFalse();
        validator.Validate(Update(null, "  ")).IsValid.Should().BeFalse();
    }
}
