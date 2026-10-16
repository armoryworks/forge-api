using FluentAssertions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

public class ConversionReadsTests
{
    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.Books.AddRange(
            new Book { Id = 1, Code = "MAIN", Name = "Main book", FunctionalCurrencyId = 3, IsActive = true },
            new Book { Id = 2, Code = "OLD", Name = "Retired book", FunctionalCurrencyId = 3, IsActive = false });
        db.GlAccounts.AddRange(
            new GlAccount { Id = 1, BookId = 1, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 2, BookId = 1, AccountNumber = "11000", Name = "Accounts receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsControlAccount = true, IsActive = true },
            new GlAccount { Id = 3, BookId = 1, AccountNumber = "10000", Name = "Current assets", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = false, IsActive = true },
            new GlAccount { Id = 4, BookId = 1, AccountNumber = "19999", Name = "Closed account", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = false },
            new GlAccount { Id = 5, BookId = 2, AccountNumber = "10100", Name = "Other book cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static ICapabilitySnapshotProvider Capabilities(bool fullGl)
    {
        var provider = new Mock<ICapabilitySnapshotProvider>();
        provider.Setup(p => p.IsEnabled("CAP-ACCT-FULLGL")).Returns(fullGl);
        return provider.Object;
    }

    [Fact]
    public async Task Accounts_listPostableAccountsOfTheBook_includingControlAccounts()
    {
        await using var db = SeededDb();

        var accounts = await new GetConversionAccountsHandler(db).Handle(new GetConversionAccountsQuery(1), default);

        accounts.Select(a => a.AccountNumber).Should().Equal("10100", "11000");
        accounts.Single(a => a.AccountNumber == "11000").IsControlAccount.Should().BeTrue();
    }

    [Fact]
    public async Task Status_withoutABook_usesTheFirstActiveBook_andReportsNothingPosted()
    {
        await using var db = SeededDb();

        var status = await new GetConversionStatusHandler(db, Capabilities(false))
            .Handle(new GetConversionStatusQuery(), default);

        status.BookId.Should().Be(1);
        status.FunctionalCurrencyId.Should().Be(3);
        status.OpeningJournalPosted.Should().BeFalse();
        status.OpeningEntryNumber.Should().BeNull();
        status.FullGlEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Status_reportsThePostedConversionEntry()
    {
        await using var db = SeededDb();
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 40, BookId = 1, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 1),
            FiscalPeriodId = 1, FiscalYearId = 1, Source = JournalSource.Conversion,
            Status = JournalEntryStatus.Posted, CurrencyId = 3,
        });
        db.SaveChanges();

        var status = await new GetConversionStatusHandler(db, Capabilities(true))
            .Handle(new GetConversionStatusQuery(1), default);

        status.OpeningJournalPosted.Should().BeTrue();
        status.OpeningEntryId.Should().Be(40);
        status.OpeningEntryNumber.Should().Be(1);
        status.OpeningEntryDate.Should().Be(new DateOnly(2026, 1, 1));
        status.FullGlEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Status_forAnInactiveBook_isNotFound()
    {
        await using var db = SeededDb();

        var act = () => new GetConversionStatusHandler(db, Capabilities(false))
            .Handle(new GetConversionStatusQuery(2), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
