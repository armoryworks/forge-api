using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §7A opening-journal import: CSV rows (account numbers) are resolved to the book's accounts and
/// posted as ONE balanced Source=Conversion entry through the engine, idempotent per book
/// (<c>Conversion:Book:{id}:OPENING</c>). Unknown account numbers are all named up-front. The
/// validator enforces the balance/XOR rules at the edge; the engine's own invariants are covered by
/// its posting tests.
/// </summary>
public class ImportOpeningJournalHandlerTests
{
    private const int BookId = 1;

    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.GlAccounts.AddRange(
            new GlAccount { Id = 1, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 20, BookId = BookId, AccountNumber = "30000", Name = "Retained Earnings", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 99, BookId = 2, AccountNumber = "77777", Name = "Other book's account", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static JournalEntry PostedEntry() => new()
    {
        Id = 10,
        BookId = BookId,
        EntryNumber = 6,
        EntryDate = new DateOnly(2026, 7, 1),
        FiscalPeriodId = 7,
        FiscalYearId = 10,
        Source = JournalSource.Conversion,
        CurrencyId = 1,
        Status = JournalEntryStatus.Posted,
        Memo = "Opening balances as of 2026-07-01 (§7A conversion)",
    };

    [Fact]
    public async Task Posts_one_balanced_conversion_entry_with_the_idempotency_key()
    {
        await using var db = SeededDb();
        var engine = new Mock<IPostingEngine>();
        PostingRequest? sent = null;
        engine
            .Setup(e => e.PostAsync(It.IsAny<PostingRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, int, CancellationToken>((r, _, _) => sent = r)
            .ReturnsAsync(PostedEntry());

        var handler = new ImportOpeningJournalHandler(db, engine.Object, HttpContextFor(7));
        var result = await handler.Handle(
            new ImportOpeningJournalCommand(BookId, new DateOnly(2026, 7, 1), CurrencyId: 1, Lines:
            [
                new OpeningJournalLineModel("10100", Debit: 10000m, Credit: 0m),
                new OpeningJournalLineModel("30000", Debit: 0m, Credit: 10000m, "Opening equity"),
            ]),
            default);

        result.EntryNumber.Should().Be(6);
        sent.Should().NotBeNull();
        sent!.Source.Should().Be(JournalSource.Conversion);
        sent.IdempotencyKey.Should().Be("Conversion:Book:1:OPENING");
        sent.SourceType.Should().Be("OpeningJournal");
        sent.Lines.Should().HaveCount(2);
        sent.Lines[0].GlAccountId.Should().Be(1);   // 10100 resolved within THIS book
        sent.Lines[1].GlAccountId.Should().Be(20);  // 30000
        engine.Verify(e => e.PostAsync(It.IsAny<PostingRequest>(), 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Names_every_unknown_account_number_up_front()
    {
        await using var db = SeededDb();
        var engine = new Mock<IPostingEngine>();
        var handler = new ImportOpeningJournalHandler(db, engine.Object, HttpContextFor(7));

        var act = () => handler.Handle(
            new ImportOpeningJournalCommand(BookId, new DateOnly(2026, 7, 1), 1, Lines:
            [
                new OpeningJournalLineModel("10100", 100m, 0m),
                new OpeningJournalLineModel("88888", 0m, 60m),
                new OpeningJournalLineModel("77777", 0m, 40m), // exists — but on book 2
            ]),
            default);

        var ex = await act.Should().ThrowAsync<PostingException>();
        ex.Which.Code.Should().Be("CONVERSION_UNKNOWN_ACCOUNTS");
        ex.Which.Message.Should().Contain("88888").And.Contain("77777");
        engine.Verify(e => e.PostAsync(It.IsAny<PostingRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_rejects_an_unbalanced_template()
    {
        var validator = new ImportOpeningJournalValidator();
        var result = validator.Validate(new ImportOpeningJournalCommand(BookId, new DateOnly(2026, 7, 1), 1, Lines:
        [
            new OpeningJournalLineModel("10100", 100m, 0m),
            new OpeningJournalLineModel("30000", 0m, 99m),
        ]));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("must balance"));
    }
}
