using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Covers the additive chart patch <c>SeedData.EnsureCashInTransitAsync</c>:
/// installs whose GL was seeded BEFORE 10150 "Cash in Transit" joined the
/// chart must get the account + CASH_IN_TRANSIT determination rule backfilled
/// on boot (the main accounting seed is run-once, guarded on a Book existing).
/// The patch runs before the run-once guard, is idempotent, and must leave
/// fresh-seed installs untouched.
/// </summary>
public class SeedAccountingCashInTransitTests
{
    private const string Key = "CASH_IN_TRANSIT";
    private const string AccountNumber = "10150";

    /// <summary>Simulates an install seeded before 10150 existed: a Book + a partial chart with no CIT account/rule.</summary>
    private static async Task<Book> SeedPreCitInstallAsync(Forge.Data.Context.AppDbContext db)
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
            Code = "MAIN",
            Name = "Default Book",
            FunctionalCurrencyId = currency.Id,
            ReportingTimeZone = "America/New_York",
            RoundingTolerance = 0.01m,
            IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        // A pre-existing seeded account + rule (CASH) that the patch must not touch.
        var cash = new GlAccount
        {
            BookId = book.Id,
            AccountNumber = "10100",
            Name = "Cash — Operating",
            AccountType = AccountType.Asset,
            NormalBalance = NormalBalance.Debit,
            IsPostable = true,
            IsActive = true,
        };
        db.GlAccounts.Add(cash);
        await db.SaveChangesAsync();

        db.AccountDeterminationRules.Add(new AccountDeterminationRule
        {
            BookId = book.Id, Key = "CASH", GlAccountId = cash.Id,
        });
        await db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task PreSeededInstall_MissingCit_GetsAccountAndRuleBackfilled()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedPreCitInstallAsync(db);

        // Run the full seed entry point — the patch must fire BEFORE the
        // run-once guard (a Book exists, so the main seed early-returns).
        await SeedData.SeedAccountingAsync(db);

        var account = await db.GlAccounts
            .SingleAsync(a => a.BookId == book.Id && a.AccountNumber == AccountNumber);
        account.Name.Should().Be("Cash in Transit");
        account.AccountType.Should().Be(AccountType.Asset);
        account.NormalBalance.Should().Be(NormalBalance.Debit);
        account.IsControlAccount.Should().BeFalse();
        account.ControlType.Should().BeNull();
        account.IsPostable.Should().BeTrue();
        account.IsActive.Should().BeTrue();

        var rule = await db.AccountDeterminationRules
            .SingleAsync(r => r.BookId == book.Id && r.Key == Key);
        rule.GlAccountId.Should().Be(account.Id);

        // The pre-existing rows were not touched; no extra chart rows appeared.
        (await db.GlAccounts.CountAsync()).Should().Be(2);
        (await db.AccountDeterminationRules.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Rerun_IsNoOp()
    {
        using var db = TestDbContextFactory.Create();
        await SeedPreCitInstallAsync(db);

        await SeedData.SeedAccountingAsync(db);
        var accountCount = await db.GlAccounts.CountAsync();
        var ruleCount = await db.AccountDeterminationRules.CountAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.GlAccounts.CountAsync()).Should().Be(accountCount, "second run must insert nothing new");
        (await db.AccountDeterminationRules.CountAsync()).Should().Be(ruleCount);
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == AccountNumber)).Should().Be(1);
        (await db.AccountDeterminationRules.CountAsync(r => r.Key == Key)).Should().Be(1);
    }

    [Fact]
    public async Task HandInsertedAccount_WithoutRule_IsReusedNotDuplicated()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedPreCitInstallAsync(db);

        // Operator already hand-inserted the account but not the rule.
        var handInserted = new GlAccount
        {
            BookId = book.Id,
            AccountNumber = AccountNumber,
            Name = "Cash in Transit",
            AccountType = AccountType.Asset,
            NormalBalance = NormalBalance.Debit,
            IsPostable = true,
            IsActive = true,
        };
        db.GlAccounts.Add(handInserted);
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.GlAccounts.CountAsync(a => a.AccountNumber == AccountNumber)).Should().Be(1);
        var rule = await db.AccountDeterminationRules.SingleAsync(r => r.BookId == book.Id && r.Key == Key);
        rule.GlAccountId.Should().Be(handInserted.Id, "the hand-inserted account must be reused");
    }

    [Fact]
    public async Task FreshSeed_IsUnaffected_AndAlreadyContainsCit()
    {
        using var db = TestDbContextFactory.Create();

        // Empty database — the patch must no-op and the run-once seed proceeds.
        await SeedData.SeedAccountingAsync(db);

        (await db.Books.CountAsync()).Should().Be(1);
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == AccountNumber)).Should().Be(1);
        (await db.AccountDeterminationRules.CountAsync(r => r.Key == Key)).Should().Be(1);

        // Re-boot: patch sees the rule and no-ops; run-once guard early-returns.
        await SeedData.SeedAccountingAsync(db);
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == AccountNumber)).Should().Be(1);
        (await db.AccountDeterminationRules.CountAsync(r => r.Key == Key)).Should().Be(1);
    }
}
