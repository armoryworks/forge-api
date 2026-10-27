using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>The boot patch that gives an existing book the consignment liability account and its determination rule.</summary>
public class ConsignmentLiabilitySeedTests
{
    private static async Task<AppDbContext> SeedBookAsync(params GlAccount[] accounts)
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, ReportingTimeZone = "UTC", IsActive = true });
        db.Set<GlAccount>().AddRange(accounts);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Boot_AddsTheConsignmentLiabilityAccountToAnExistingBook()
    {
        using var db = await SeedBookAsync();

        await SeedData.EnsureDeterminationAccountsAsync(db);

        var rule = await db.AccountDeterminationRules.Include(r => r.GlAccount)
            .SingleAsync(r => r.Key == "CONSIGNMENT_LIABILITY");
        rule.GlAccount.AccountNumber.Should().Be("21100");
        rule.GlAccount.AccountType.Should().Be(AccountType.Liability);
        rule.GlAccount.NormalBalance.Should().Be(NormalBalance.Credit);
    }

    [Fact]
    public async Task Boot_MapsAnExistingConsignmentLiabilityAccountWithoutAddingAnother()
    {
        using var db = await SeedBookAsync(new GlAccount
        {
            Id = 50, BookId = 1, AccountNumber = "21100", Name = "Consignment Liability",
            AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true,
        });

        await SeedData.EnsureDeterminationAccountsAsync(db);
        await SeedData.EnsureDeterminationAccountsAsync(db);

        (await db.AccountDeterminationRules.Where(r => r.Key == "CONSIGNMENT_LIABILITY").Select(r => r.GlAccountId).ToListAsync())
            .Should().Equal(50);
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == "21100")).Should().Be(1);
    }
}
