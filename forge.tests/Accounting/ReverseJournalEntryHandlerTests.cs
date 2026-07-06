using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5A "Reverse / correct" endpoint handler: a thin exposure of <see cref="IPostingEngine.ReverseAsync"/>.
/// It must pass the <b>server-trusted</b> user id (from the JWT, never the client) plus the entry id,
/// date, and reason to the engine, and map the engine's reversal entry to the response. The engine owns
/// the preconditions and SoD — those are covered by the engine's own atomicity tests.
/// </summary>
public class ReverseJournalEntryHandlerTests
{
    private static IHttpContextAccessor HttpContextFor(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    [Fact]
    public async Task Reverses_via_the_engine_with_the_server_trusted_user_and_maps_the_result()
    {
        var reversal = new JournalEntry
        {
            Id = 99,
            BookId = 1,
            EntryNumber = 12,
            EntryDate = new DateOnly(2026, 2, 1),
            FiscalPeriodId = 1000,
            FiscalYearId = 10,
            Source = JournalSource.Manual,
            CurrencyId = 1,
            Status = JournalEntryStatus.Posted,
            Memo = "Reversal of #11",
            Lines = new[]
            {
                new JournalLine { Id = 1, BookId = 1, LineNumber = 1, GlAccountId = 100, Credit = 50m, FunctionalAmount = 50m },
                new JournalLine { Id = 2, BookId = 1, LineNumber = 2, GlAccountId = 101, Debit = 50m, FunctionalAmount = 50m },
            },
        };
        var engine = new Mock<IPostingEngine>();
        engine
            .Setup(e => e.ReverseAsync(11, It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reversal);

        var handler = new ReverseJournalEntryHandler(engine.Object, HttpContextFor(7));
        var result = await handler.Handle(new ReverseJournalEntryCommand(11, new DateOnly(2026, 2, 1), "wrong account"), default);

        result.Id.Should().Be(99);
        result.EntryNumber.Should().Be(12);
        result.Lines.Should().HaveCount(2);
        engine.Verify(
            e => e.ReverseAsync(11, new DateOnly(2026, 2, 1), "wrong account", 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
