using System.Reflection;
using System.Text.RegularExpressions;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Features.Accounting.Training;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Tests.Helpers;

namespace Forge.Tests.Seed;

/// <summary>
/// Covers the payroll and FX revaluation determination keys: a fresh seed maps every key the posting services
/// reference, and the boot patch backfills them on installs seeded earlier, at account numbers that are free,
/// never by taking over a tenant's own account that happens to use the same number.
/// </summary>
public class SeedAccountingDeterminationPatchTests
{
    private static readonly string[] PatchedKeys =
    [
        "FX_REVALUATION", "EMPLOYEE_TAX_PAYABLE", "EMPLOYER_TAX_PAYABLE", "NET_PAY_PAYABLE",
        "WAGE_EXPENSE", "EMPLOYER_PAYROLL_TAX_EXPENSE",
    ];

    private static IReadOnlyCollection<string> KeysReferencedByPostingServices()
        => typeof(Forge.Api.Features.Accounting.PayrollService).Assembly.GetTypes()
            .Where(t => t.Namespace == "Forge.Api.Features.Accounting")
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.StartsWith("Key", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => Regex.IsMatch(v, "^[A-Z_]+$"))
            .ToHashSet();

    private static async Task<Book> SeedPrePatchInstallAsync(Forge.Data.Context.AppDbContext db, string code = "MAIN")
    {
        var currency = await db.Currencies.FirstOrDefaultAsync();
        if (currency is null)
        {
            currency = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true, IsActive = true, SortOrder = 1 };
            db.Currencies.Add(currency);
            await db.SaveChangesAsync();
        }

        var book = new Book
        {
            Code = code, Name = code, FunctionalCurrencyId = currency.Id,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();

        var cash = new GlAccount
        {
            BookId = book.Id, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset,
            NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        };
        db.GlAccounts.Add(cash);
        await db.SaveChangesAsync();
        db.AccountDeterminationRules.Add(new AccountDeterminationRule { BookId = book.Id, Key = "CASH", GlAccountId = cash.Id });
        await db.SaveChangesAsync();
        return book;
    }

    [Fact]
    public async Task FreshSeed_MapsEveryDeterminationKeyThePostingServicesReference()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedAccountingAsync(db);

        var mapped = await db.AccountDeterminationRules.Select(r => r.Key).ToListAsync();
        var referenced = KeysReferencedByPostingServices();
        referenced.Should().Contain(PatchedKeys);
        referenced.Except(mapped).Should().BeEmpty("a posting to an unmapped key fails with DETERMINATION_UNMAPPED");
    }

    [Fact]
    public async Task FreshSeed_PayrollAndFxAccounts_UseNumbersOutsideTheCommonChartAdditions()
    {
        using var db = TestDbContextFactory.Create();

        await SeedData.SeedAccountingAsync(db);

        var byKey = await db.AccountDeterminationRules
            .Join(db.GlAccounts, r => r.GlAccountId, a => a.Id, (r, a) => new { r.Key, a.AccountNumber, a.AccountType })
            .ToDictionaryAsync(x => x.Key);
        byKey["WAGE_EXPENSE"].AccountNumber.Should().Be("66000");
        byKey["EMPLOYER_PAYROLL_TAX_EXPENSE"].AccountNumber.Should().Be("66100");
        byKey["EMPLOYEE_TAX_PAYABLE"].AccountNumber.Should().Be("26200");
        byKey["EMPLOYER_TAX_PAYABLE"].AccountNumber.Should().Be("26300");
        byKey["NET_PAY_PAYABLE"].AccountNumber.Should().Be("26400");
        byKey["FX_REVALUATION"].AccountNumber.Should().Be("19900");
        byKey["FX_REVALUATION"].AccountType.Should().Be(AccountType.Asset);
        (await db.GlAccounts.AnyAsync(a => a.AccountNumber == "61000")).Should().BeFalse(
            "61000 is left free for the tenant's own chart (often Utilities)");
    }

    [Fact]
    public async Task PreSeededInstall_GetsMissingKeysBackfilled_AndRerunIsNoOp()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedPrePatchInstallAsync(db);

        await SeedData.SeedAccountingAsync(db);
        var accounts = await db.GlAccounts.CountAsync();
        var rules = await db.AccountDeterminationRules.CountAsync();
        await SeedData.SeedAccountingAsync(db);

        foreach (var key in PatchedKeys)
            (await db.AccountDeterminationRules.CountAsync(r => r.BookId == book.Id && r.Key == key)).Should().Be(1, key);
        (await db.GlAccounts.CountAsync()).Should().Be(accounts);
        (await db.AccountDeterminationRules.CountAsync()).Should().Be(rules);
    }

    [Fact]
    public async Task NumberAlreadyTaken_SkipsTheKey_AndNeverRepurposesTheTenantsAccount()
    {
        using var db = TestDbContextFactory.Create();
        var book = await SeedPrePatchInstallAsync(db);
        var tenants = new GlAccount
        {
            BookId = book.Id, AccountNumber = "66000", Name = "Utilities", AccountType = AccountType.Expense,
            NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        };
        db.GlAccounts.Add(tenants);
        await db.SaveChangesAsync();

        await SeedData.SeedAccountingAsync(db);

        (await db.AccountDeterminationRules.AnyAsync(r => r.Key == "WAGE_EXPENSE")).Should().BeFalse();
        (await db.GlAccounts.SingleAsync(a => a.Id == tenants.Id)).Name.Should().Be("Utilities");
        (await db.GlAccounts.CountAsync(a => a.AccountNumber == "66000")).Should().Be(1);
        (await db.AccountDeterminationRules.AnyAsync(r => r.Key == "EMPLOYER_PAYROLL_TAX_EXPENSE")).Should().BeTrue();
    }

    [Fact]
    public async Task TrainingSandboxBook_IsLeftAlone()
    {
        using var db = TestDbContextFactory.Create();
        await SeedPrePatchInstallAsync(db);
        var training = await SeedPrePatchInstallAsync(db, TrainingSandboxService.BookCode);

        await SeedData.SeedAccountingAsync(db);

        (await db.AccountDeterminationRules.AnyAsync(r => r.BookId == training.Id && PatchedKeys.Contains(r.Key))).Should().BeFalse();
    }
}
