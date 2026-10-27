using FluentAssertions;
using Moq;
using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

/// <summary>
/// Valid-until edits on UpdateQuote: Draft quotes edit freely, Sent quotes can be extended (logged),
/// and an Expired quote extended to a future date returns to Sent. Other fields stay Draft-only.
/// </summary>
public class UpdateQuoteValidUntilTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);

    private readonly Mock<IQuoteRepository> _repo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateQuoteHandler _handler;

    public UpdateQuoteValidUntilTests()
    {
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        _handler = new UpdateQuoteHandler(
            _repo.Object, new Mock<ISystemSettingRepository>().Object, new Mock<IBusinessIdentifierService>().Object, _db, clock.Object);
    }

    private Quote Given(QuoteStatus status, DateTimeOffset? validUntil)
    {
        var quote = new Quote { Id = 7, QuoteNumber = "Q-7", Status = status, ExpirationDate = validUntil };
        _repo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(quote);
        return quote;
    }

    private static UpdateQuoteCommand ValidUntil(DateTimeOffset date) => new(7, null, date, null, null);

    [Fact]
    public async Task Draft_quote_takes_a_new_valid_until()
    {
        var quote = Given(QuoteStatus.Draft, Now.AddDays(10));

        await _handler.Handle(ValidUntil(Now.AddDays(30)), CancellationToken.None);

        quote.ExpirationDate.Should().Be(Now.AddDays(30));
        quote.Status.Should().Be(QuoteStatus.Draft);
    }

    [Fact]
    public async Task Sent_quote_is_extended_and_the_extension_is_logged()
    {
        var quote = Given(QuoteStatus.Sent, Now.AddDays(2));

        await _handler.Handle(ValidUntil(Now.AddDays(45)), CancellationToken.None);

        quote.ExpirationDate.Should().Be(Now.AddDays(45));
        quote.Status.Should().Be(QuoteStatus.Sent);
        _db.ActivityLogs.Local.Should().ContainSingle(a => a.EntityType == "Quote" && a.EntityId == 7)
            .Which.Description.Should().Contain("valid-until extended");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Expired_quote_extended_returns_to_sent()
    {
        var quote = Given(QuoteStatus.Expired, Now.AddDays(-5));

        await _handler.Handle(ValidUntil(Now.AddDays(14)), CancellationToken.None);

        quote.Status.Should().Be(QuoteStatus.Sent);
        quote.ExpirationDate.Should().Be(Now.AddDays(14));
        _db.ActivityLogs.Local.Should().ContainSingle().Which.Description.Should().Contain("returned to Sent");
    }

    [Fact]
    public async Task Sent_quote_cannot_move_valid_until_into_the_past()
    {
        var quote = Given(QuoteStatus.Sent, Now.AddDays(2));

        var act = () => _handler.Handle(ValidUntil(Now.AddDays(-1)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        quote.ExpirationDate.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public async Task Sent_quote_still_refuses_other_field_edits()
    {
        Given(QuoteStatus.Sent, Now.AddDays(2));

        var act = () => _handler.Handle(new UpdateQuoteCommand(7, null, Now.AddDays(20), "new notes", null), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Only Draft");
    }

    [Fact]
    public async Task Accepted_quote_cannot_be_extended()
    {
        Given(QuoteStatus.Accepted, Now.AddDays(2));

        var act = () => _handler.Handle(ValidUntil(Now.AddDays(20)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
