using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5A chart-of-accounts read endpoint: the account pick-list for the manual-entry editor. Active
/// accounts for the book, ordered by number; <c>postableOnly</c> drops control accounts (which post
/// only via sub-ledgers). Hand-seeded — this proves the filter/projection, not posting.
/// </summary>
public class GetChartOfAccountsHandlerTests
{
    private const int BookId = 1;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.GlAccounts.AddRange(
            Account(100, "1000", "Cash", AccountType.Asset, NormalBalance.Debit, postable: true, control: false, active: true),
            Account(102, "1100", "AR Control", AccountType.Asset, NormalBalance.Debit, postable: true, control: true, active: true),
            Account(101, "4000", "Revenue", AccountType.Income, NormalBalance.Credit, postable: true, control: false, active: true),
            Account(199, "9999", "Retired", AccountType.Expense, NormalBalance.Debit, postable: true, control: false, active: false),
            Account(200, "5000", "COGS (other book)", AccountType.Expense, NormalBalance.Debit, postable: true, control: false, active: true, bookId: 2));
        db.SaveChanges();
        return db;
    }

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal,
        bool postable, bool control, bool active, int bookId = BookId) => new()
    {
        Id = id,
        BookId = bookId,
        AccountNumber = number,
        Name = name,
        AccountType = type,
        NormalBalance = normal,
        IsPostable = postable,
        IsControlAccount = control,
        IsActive = active,
    };

    [Fact]
    public async Task Returns_active_book_accounts_ordered_by_number()
    {
        await using var db = SeededDb();
        var result = await new GetChartOfAccountsHandler(db).Handle(new GetChartOfAccountsQuery(BookId), default);

        result.Select(a => a.AccountNumber).Should().ContainInOrder("1000", "1100", "4000");
        result.Should().NotContain(a => a.AccountNumber == "9999"); // inactive excluded
        result.Should().NotContain(a => a.AccountNumber == "5000"); // other book excluded
    }

    [Fact]
    public async Task PostableOnly_excludes_control_accounts()
    {
        await using var db = SeededDb();
        var result = await new GetChartOfAccountsHandler(db).Handle(new GetChartOfAccountsQuery(BookId, PostableOnly: true), default);

        result.Select(a => a.AccountNumber).Should().BeEquivalentTo(new[] { "1000", "4000" });
        result.Should().NotContain(a => a.IsControlAccount);
    }

    [Fact]
    public async Task Maps_enums_to_strings()
    {
        await using var db = SeededDb();
        var result = await new GetChartOfAccountsHandler(db).Handle(new GetChartOfAccountsQuery(BookId), default);

        var revenue = result.Single(a => a.AccountNumber == "4000");
        revenue.AccountType.Should().Be("Income");
        revenue.NormalBalance.Should().Be("Credit");
    }
}
