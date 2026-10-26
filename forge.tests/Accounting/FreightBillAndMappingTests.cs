using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Accounting.Determination;
using Forge.Api.Features.Freight;
using Forge.Api.Features.VendorBills;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>Vendor-bill freight line validation and the Controller's account-mapping endpoint for the freight keys.</summary>
public class FreightBillAndMappingTests
{
    private static CreateVendorBillCommand Bill(int? poId, params CreateVendorBillLineModel[] lines)
        => new(1, "V-1", poId, new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero), 0m, null, [.. lines]);

    [Fact]
    public void FreightLineOnAPoBill_NeedsNoPoLine()
    {
        var result = new CreateVendorBillValidator().Validate(Bill(9,
            new CreateVendorBillLineModel(null, "Inbound freight", 1, 55m, null, LineType: DocumentLineType.Freight)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void FreightLine_CannotNameAPartOrAPoLine()
    {
        var result = new CreateVendorBillValidator().Validate(Bill(null,
            new CreateVendorBillLineModel(4, "Freight", 1, 55m, null, PurchaseOrderLineId: 8, LineType: DocumentLineType.Freight)));

        result.Errors.Should().Contain(e => e.ErrorCode == FreightLines.PartCode);
        result.Errors.Should().Contain(e => e.ErrorCode == FreightLines.PoLineCode);
    }

    [Fact]
    public void ItemLine_CannotClearAPosFreight()
    {
        var result = new CreateVendorBillValidator().Validate(Bill(null,
            new CreateVendorBillLineModel(null, "Bolts", 1, 5m, "OPERATING_EXPENSE", FreightForPurchaseOrderId: 3)));

        result.IsValid.Should().BeFalse();
    }

    private static async Task<AppDbContext> SeedBookAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, ReportingTimeZone = "UTC", IsActive = true });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = 10, BookId = 1, AccountNumber = "40500", Name = "Freight Income", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 11, BookId = 1, AccountNumber = "40600", Name = "Delivery Charges", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 12, BookId = 1, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 13, BookId = 1, AccountNumber = "11000", Name = "AR", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = 1, Key = "FREIGHT_INCOME", GlAccountId = 10 });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Mapping_RepointsFreightIncome_ToAnotherIncomeAccount()
    {
        using var db = await SeedBookAsync();

        var result = await new UpdateDeterminationRuleHandler(db).Handle(new UpdateDeterminationRuleCommand("FREIGHT_INCOME", 11), CancellationToken.None);

        result.AccountNumber.Should().Be("40600");
        (await db.AccountDeterminationRules.SingleAsync(r => r.Key == "FREIGHT_INCOME")).GlAccountId.Should().Be(11);
    }

    [Theory]
    [InlineData(12, "determination-account-type")]
    [InlineData(13, "determination-account-control")]
    [InlineData(999, "determination-account-not-found")]
    public async Task Mapping_RefusesAnIncompatibleAccount(int accountId, string code)
    {
        using var db = await SeedBookAsync();

        var act = () => new UpdateDeterminationRuleHandler(db).Handle(new UpdateDeterminationRuleCommand("FREIGHT_INCOME", accountId), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(e => e.ErrorCode == code);
    }

    [Fact]
    public void Mapping_RefusesSystemManagedKeys()
        => new UpdateDeterminationRuleValidator().Validate(new UpdateDeterminationRuleCommand("AR_CONTROL", 13))
            .Errors.Should().Contain(e => e.ErrorCode == "determination-key-not-configurable");

    [Fact]
    public async Task Rules_ListUnmappedConfigurableKeys()
    {
        using var db = await SeedBookAsync();

        var rules = await new GetDeterminationRulesHandler(db).Handle(new GetDeterminationRulesQuery(), CancellationToken.None);

        rules.Single(r => r.Key == "FREIGHT_INCOME").AccountNumber.Should().Be("40500");
        rules.Single(r => r.Key == "FREIGHT_IN").Should().Match<Forge.Core.Models.Accounting.DeterminationRuleModel>(r =>
            r.GlAccountId == null && r.Configurable && r.RequiredAccountType == "Expense");
    }

    [Fact]
    public async Task Boot_AddsTheFreightAccountsToAnExistingBook()
    {
        using var db = await SeedBookAsync();

        await SeedData.EnsureDeterminationAccountsAsync(db);

        var mapped = await db.AccountDeterminationRules.Include(r => r.GlAccount)
            .Where(r => r.Key.StartsWith("FREIGHT_"))
            .ToDictionaryAsync(r => r.Key, r => r.GlAccount.AccountNumber);
        mapped.Should().Contain(new Dictionary<string, string>
        {
            ["FREIGHT_INCOME"] = "40500",
            ["FREIGHT_IN"] = "50100",
            ["FREIGHT_OUT"] = "61200",
        });
    }
}
