using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Covers the additive chart patch <c>SeedData.EnsureKeyedAccountAsync</c> as wired for FIXED_ASSETS (15000) and
/// GAIN_LOSS_ON_DISPOSAL (91000): a pre-seeded book gets the account + rule, an existing same-numbered account is
/// reused only when its type fits, a mistyped one leaves the key unset, and a rerun inserts nothing.
/// </summary>
public class SeedAccountingKeyedAccountTests
{
    private static async Task<Book> SeedBookAsync(AppDbContext db)
    {
        var currency = new Currency
        {
            Code = "USD", Name = "US Dollar", Symbol = "$",
            DecimalPlaces = 2, IsBaseCurrency = true, IsActive = true, SortOrder = 1,
        };
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();

        var book = new Book
        {
            Code = "MAIN", Name = "Default Book", FunctionalCurrencyId = currency.Id,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    private static GlAccount Account(int bookId, string number, string name, AccountType type, NormalBalance normal) => new()
    {
        BookId = bookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = normal,
        IsPostable = true, IsActive = true,
    };

    [Fact]
    public async Task PreSeededBook_GetsFixedAssetAndDisposalAccountsWithRules()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedBookAsync(db);

        await SeedData.SeedAccountingAsync(db);

        var fixedAssets = await db.AccountDeterminationRules.Include(r => r.GlAccount)
            .SingleAsync(r => r.BookId == book.Id && r.Key == "FIXED_ASSETS");
        fixedAssets.GlAccount.AccountNumber.Should().Be("15000");
        fixedAssets.GlAccount.AccountType.Should().Be(AccountType.Asset);

        var gainLoss = await db.AccountDeterminationRules.Include(r => r.GlAccount)
            .SingleAsync(r => r.BookId == book.Id && r.Key == "GAIN_LOSS_ON_DISPOSAL");
        gainLoss.GlAccount.AccountNumber.Should().Be("91000");
        gainLoss.GlAccount.AccountType.Should().Be(AccountType.Income);
    }

    [Fact]
    public async Task ExistingAssetAccount_IsReused()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedBookAsync(db);
        var existing = Account(book.Id, "15000", "Machinery and equipment at cost", AccountType.Asset, NormalBalance.Debit);
        db.GlAccounts.Add(existing);
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.AccountDeterminationRules.SingleAsync(r => r.Key == "FIXED_ASSETS")).GlAccountId.Should().Be(existing.Id);
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == "15000")).Should().Be(1);
    }

    [Fact]
    public async Task ExistingAccountOfAnotherType_LeavesTheKeyUnset()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedBookAsync(db);
        db.GlAccounts.Add(Account(book.Id, "15000", "Freight income", AccountType.Income, NormalBalance.Credit));
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.AccountDeterminationRules.AnyAsync(r => r.Key == "FIXED_ASSETS")).Should().BeFalse();
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == "15000")).Should().Be(1);
    }

    [Fact]
    public async Task Rerun_IsNoOp()
    {
        using var db = TestDbContextFactory.Create();
        await SeedBookAsync(db);
        await SeedData.SeedAccountingAsync(db);
        var accounts = await db.GlAccounts.CountAsync();
        var rules = await db.AccountDeterminationRules.CountAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.GlAccounts.CountAsync()).Should().Be(accounts);
        (await db.AccountDeterminationRules.CountAsync()).Should().Be(rules);
    }
}
