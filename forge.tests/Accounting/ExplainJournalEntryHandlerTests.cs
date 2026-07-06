using FluentAssertions;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Accounting-AI advisory (§5A): read-only "explain this journal entry". Proves the guardrail — the
/// handler narrates via the assistant when available and degrades to a deterministic, non-AI summary
/// when it isn't; it never posts (it has no <c>IPostingEngine</c> dependency to post with).
/// </summary>
public class ExplainJournalEntryHandlerTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const long EntryId = 5001;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();

        db.GlAccounts.AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });

        db.JournalEntries.Add(new JournalEntry
        {
            Id = EntryId,
            BookId = BookId,
            EntryNumber = 42,
            EntryDate = new DateOnly(2026, 1, 10),
            FiscalPeriodId = 1000,
            FiscalYearId = 10,
            Source = JournalSource.Manual,
            CurrencyId = UsdId,
            Status = JournalEntryStatus.Posted,
            Memo = "Cash sale",
            Lines = new[]
            {
                new JournalLine { Id = 1, BookId = BookId, LineNumber = 1, GlAccountId = CashId, Debit = 100m, CurrencyId = UsdId, TxnAmount = 100m, FunctionalAmount = 100m, FxRate = 1m },
                new JournalLine { Id = 2, BookId = BookId, LineNumber = 2, GlAccountId = RevenueId, Credit = 100m, CurrencyId = UsdId, TxnAmount = 100m, FunctionalAmount = 100m, FxRate = 1m },
            },
        });

        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Uses_AI_narrative_when_assistant_available()
    {
        await using var db = SeededDb();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(x => x.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This entry records a $100 cash sale.");

        var result = await new ExplainJournalEntryHandler(db, ai.Object)
            .Handle(new ExplainJournalEntryQuery(BookId, EntryId), default);

        result.AiAvailable.Should().BeTrue();
        result.Explanation.Should().Be("This entry records a $100 cash sale.");
        result.DeterministicSummary.Should().Contain("Entry #42").And.Contain("Cash");
    }

    [Fact]
    public async Task Degrades_to_deterministic_summary_when_assistant_offline()
    {
        await using var db = SeededDb();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await new ExplainJournalEntryHandler(db, ai.Object)
            .Handle(new ExplainJournalEntryQuery(BookId, EntryId), default);

        result.AiAvailable.Should().BeFalse();
        result.Explanation.Should().Be(result.DeterministicSummary);
        result.Explanation.Should().Contain("Dr 1000 Cash 100").And.Contain("Cr 4000 Revenue 100");
        ai.Verify(x => x.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_entry_throws_KeyNotFound()
    {
        await using var db = SeededDb();
        var ai = new Mock<IAiService>();
        ai.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => new ExplainJournalEntryHandler(db, ai.Object)
            .Handle(new ExplainJournalEntryQuery(BookId, 999999), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
