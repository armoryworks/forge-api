using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

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
        validator.Validate(Update(null, "  ")).IsValid.Should().BeFalse();
        validator.Validate(Update(new string('9', 21), "Supplies")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Update_KeepsALegacyNumber_WhenOnlyTheNameChanges()
    {
        using var db = await SeedLegacyAccountAsync();

        var updated = await new UpdateGlAccountHandler(db).Handle(
            Update("60 100_A", "Shop Supplies") with { Id = LegacyId }, CancellationToken.None);

        updated.Name.Should().Be("Shop Supplies");
        updated.AccountNumber.Should().Be("60 100_A");
    }

    [Fact]
    public async Task Update_RejectsAMalformedNumber_WhenTheNumberChanges()
    {
        using var db = await SeedLegacyAccountAsync();

        var act = () => new UpdateGlAccountHandler(db).Handle(
            Update("60 200", "Shop Supplies") with { Id = LegacyId }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await db.GlAccounts.SingleAsync(a => a.Id == LegacyId)).AccountNumber.Should().Be("60 100_A");
    }

    [Fact]
    public async Task Update_AcceptsAWellFormedNewNumber()
    {
        using var db = await SeedLegacyAccountAsync();

        var updated = await new UpdateGlAccountHandler(db).Handle(
            Update("60100", "Shop Supplies") with { Id = LegacyId }, CancellationToken.None);

        updated.AccountNumber.Should().Be("60100");
    }

    private const int LegacyId = 500;

    private static async Task<AppDbContext> SeedLegacyAccountAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<GlAccount>().Add(new GlAccount
        {
            Id = LegacyId, BookId = 1, AccountNumber = "60 100_A", Name = "Supplies",
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        });
        await db.SaveChangesAsync();
        return db;
    }
}
