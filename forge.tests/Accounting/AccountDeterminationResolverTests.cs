using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-0 account-determination resolver coverage (§4, §5.2): resolves
/// <c>(BookId, Key[, scope])</c> to a postable/active/in-book account;
/// most-specific scope wins; an unmapped key is a hard <see cref="PostingException"/>;
/// there is NO silent cross-book fallback (the lookup is book-scoped and a
/// target in another book is rejected). Complements the engine-level
/// determination tests in <see cref="PostingEngineTests"/> by exercising the
/// resolver in isolation.
/// </summary>
public class AccountDeterminationResolverTests
{
    private const int BookId = 1;
    private const int OtherBookId = 2;
    private const int UsdId = 1;

    private const int CashId = 100;        // Asset, postable, in BookId
    private const int RevenueId = 101;     // Income, postable, in BookId
    private const int InventoryFgId = 102; // global COGS/INVENTORY target
    private const int ItemScopedId = 103;  // item-scoped INVENTORY_FG target
    private const int CategoryScopedId = 104;
    private const int NonPostableId = 105; // header
    private const int InactiveId = 106;    // inactive
    private const int OtherBookAcctId = 200;

    private const int ScopeItemId = 5000;
    private const int ScopeCategoryId = 6000;

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

        db.Set<Book>().AddRange(
            new Book
            {
                Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
                ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            },
            new Book
            {
                Id = OtherBookId, Code = "OTHER", Name = "Other", FunctionalCurrencyId = UsdId,
                ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            });

        db.Set<GlAccount>().AddRange(
            Account(CashId, "1000", "Cash", AccountType.Asset, NormalBalance.Debit),
            Account(RevenueId, "4000", "Revenue", AccountType.Income, NormalBalance.Credit),
            Account(InventoryFgId, "1300", "Finished Goods (global)", AccountType.Asset, NormalBalance.Debit),
            Account(ItemScopedId, "1301", "Finished Goods (item)", AccountType.Asset, NormalBalance.Debit),
            Account(CategoryScopedId, "1302", "Finished Goods (category)", AccountType.Asset, NormalBalance.Debit),
            new GlAccount
            {
                Id = NonPostableId, BookId = BookId, AccountNumber = "1", Name = "Assets (summary)",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = false, IsActive = true,
            },
            new GlAccount
            {
                Id = InactiveId, BookId = BookId, AccountNumber = "9999", Name = "Old Account",
                AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = false,
            },
            new GlAccount
            {
                Id = OtherBookAcctId, BookId = OtherBookId, AccountNumber = "1000", Name = "Other Cash",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = true,
            });

        db.Set<AccountDeterminationRule>().AddRange(
            // Plain global rows.
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },

            // Three rows for the SAME key at increasing specificity (global <
            // category-scoped < item-scoped) so most-specific-wins is observable.
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InventoryFgId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = CategoryScopedId, CategoryId = ScopeCategoryId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = ItemScopedId, ItemId = ScopeItemId },

            // Misconfigured targets — resolvable rule, unusable account.
            new AccountDeterminationRule { BookId = BookId, Key = "NON_POSTABLE_TARGET", GlAccountId = NonPostableId },
            new AccountDeterminationRule { BookId = BookId, Key = "INACTIVE_TARGET", GlAccountId = InactiveId },

            // A rule whose target account lives in another book (forbidden fallback).
            new AccountDeterminationRule { BookId = BookId, Key = "CROSS_BOOK_TARGET", GlAccountId = OtherBookAcctId },

            // The OTHER book maps CASH to its own account; resolving CASH for
            // BookId must NEVER fall through to this row.
            new AccountDeterminationRule { BookId = OtherBookId, Key = "CASH", GlAccountId = OtherBookAcctId });

        await db.SaveChangesAsync();
        return db;
    }

    private static GlAccount Account(int id, string num, string name, AccountType type, NormalBalance nb)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = num, Name = name,
            AccountType = type, NormalBalance = nb, IsPostable = true, IsActive = true,
        };

    [Fact]
    public async Task ResolveAsync_GlobalKey_ReturnsMappedAccount()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var account = await resolver.ResolveAsync(BookId, "CASH");

        account.Id.Should().Be(CashId);
        account.BookId.Should().Be(BookId);
    }

    [Fact]
    public async Task ResolveAsync_UnmappedKey_ThrowsHardError()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var act = async () => await resolver.ResolveAsync(BookId, "DOES_NOT_EXIST");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");
    }

    [Fact]
    public async Task ResolveAsync_EmptyKey_Throws()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var act = async () => await resolver.ResolveAsync(BookId, "   ");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_KEY_EMPTY");
    }

    [Fact]
    public async Task ResolveAsync_NoScopeSupplied_FallsBackToGlobalRow()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // Three INVENTORY_FG rows exist; with no scope args only the global
        // (all-null-scope) row matches.
        var account = await resolver.ResolveAsync(BookId, "INVENTORY_FG");

        account.Id.Should().Be(InventoryFgId);
    }

    [Fact]
    public async Task ResolveAsync_ItemScope_MostSpecificWins()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // Item arg matches both the global row (null wildcard) and the item row;
        // the item-scoped row is more specific and must win.
        var account = await resolver.ResolveAsync(BookId, "INVENTORY_FG", itemId: ScopeItemId);

        account.Id.Should().Be(ItemScopedId);
    }

    [Fact]
    public async Task ResolveAsync_CategoryScope_MostSpecificWins()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var account = await resolver.ResolveAsync(BookId, "INVENTORY_FG", categoryId: ScopeCategoryId);

        account.Id.Should().Be(CategoryScopedId);
    }

    [Fact]
    public async Task ResolveAsync_NonMatchingScope_StillResolvesToGlobalWildcard()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // An item arg that matches no item-scoped row still resolves via the
        // global (null-scope wildcard) row — the scoped rows are filtered out.
        var account = await resolver.ResolveAsync(BookId, "INVENTORY_FG", itemId: 999_999);

        account.Id.Should().Be(InventoryFgId);
    }

    [Fact]
    public async Task ResolveAsync_CrossBookTarget_ThrowsAndDoesNotFallBack()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // The BookId rule for this key points at an account in OtherBook.
        var act = async () => await resolver.ResolveAsync(BookId, "CROSS_BOOK_TARGET");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_CROSS_BOOK");
    }

    [Fact]
    public async Task ResolveAsync_NoCrossBookFallback_OtherBooksRuleIsNotConsulted()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // Removing BookId's own CASH rule leaves only OtherBook's CASH rule.
        // Resolving CASH for BookId must NOT find the OtherBook row — it is
        // unmapped for this book, not a silent cross-book hit.
        var ownRule = db.Set<AccountDeterminationRule>()
            .Single(r => r.BookId == BookId && r.Key == "CASH");
        db.Set<AccountDeterminationRule>().Remove(ownRule);
        await db.SaveChangesAsync();

        var act = async () => await resolver.ResolveAsync(BookId, "CASH");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");
    }

    [Fact]
    public async Task ResolveAsync_NonPostableTarget_Throws()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var act = async () => await resolver.ResolveAsync(BookId, "NON_POSTABLE_TARGET");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_NOT_POSTABLE");
    }

    [Fact]
    public async Task ResolveAsync_InactiveTarget_Throws()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var act = async () => await resolver.ResolveAsync(BookId, "INACTIVE_TARGET");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_INACTIVE");
    }

    [Fact]
    public async Task ResolveAsync_MissingTargetAccount_Throws()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        // A rule pointing at a GlAccountId that does not exist at all.
        db.Set<AccountDeterminationRule>().Add(
            new AccountDeterminationRule { BookId = BookId, Key = "DANGLING", GlAccountId = 777_777 });
        await db.SaveChangesAsync();

        var act = async () => await resolver.ResolveAsync(BookId, "DANGLING");
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_ACCOUNT_MISSING");
    }

    [Fact]
    public async Task ValidateKeysAsync_ReturnsOnlyTheFailingKeys()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var failed = await resolver.ValidateKeysAsync(
            BookId,
            ["CASH", "SALES_REVENUE", "DOES_NOT_EXIST", "NON_POSTABLE_TARGET", "CROSS_BOOK_TARGET"]);

        // Good keys are absent; every unresolvable/unusable key is reported.
        failed.Should().BeEquivalentTo(["DOES_NOT_EXIST", "NON_POSTABLE_TARGET", "CROSS_BOOK_TARGET"]);
    }

    [Fact]
    public async Task ValidateKeysAsync_AllResolvable_ReturnsEmpty()
    {
        using var db = await SeedAsync();
        var resolver = new AccountDeterminationResolver(db);

        var failed = await resolver.ValidateKeysAsync(BookId, ["CASH", "SALES_REVENUE", "INVENTORY_FG"]);

        failed.Should().BeEmpty();
    }
}
