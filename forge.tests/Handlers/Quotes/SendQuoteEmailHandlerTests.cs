using System.Text.RegularExpressions;

using FluentAssertions;
using MediatR;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Quotes;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Quotes;

public class SendQuoteEmailHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IIntegrationOutboxService> _outbox = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly IClock _clock = new SystemClock();
    private readonly SendQuoteEmailHandler _handler;

    public SendQuoteEmailHandlerTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        _settings.Setup(s => s.FindByKeyAsync("company_name", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);
        _outbox.Setup(o => o.EnqueueEmailAsync(
                It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntegrationOutboxEntry());
        _mediator.Setup(m => m.Send(It.IsAny<SendQuoteCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _handler = new SendQuoteEmailHandler(
            _db, _settings.Object, _outbox.Object,
            new TermsCompilationService(_db, _clock), _clock, _mediator.Object);
    }

    private async Task<Quote> SeedQuoteAsync(QuoteStatus status)
    {
        var customer = new Customer { Name = "Acme Corp", Email = "buyer@acme.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var quote = new Quote
        {
            CustomerId = customer.Id,
            QuoteNumber = "Q-1001",
            Status = status,
            Lines =
            {
                new QuoteLine { LineNumber = 1, Description = "Widget", Quantity = 5, UnitPrice = 10m },
            },
        };
        _db.Quotes.Add(quote);

        _db.TermsDocuments.Add(new TermsDocument
        {
            Scope = TermsScope.Company,
            Title = "Standard Terms",
            BodyMarkdown = "All sales are final.",
            EffectiveFrom = _clock.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();
        return quote;
    }

    [Fact]
    public async Task Handle_DraftQuote_EnqueuesEmail_PersistsSnapshot_AndFlipsStatusViaSendQuote()
    {
        var quote = await SeedQuoteAsync(QuoteStatus.Draft);
        var command = new SendQuoteEmailCommand(quote.Id, "buyer@acme.test", "Thanks!", "https://forge.example.com");

        await _handler.Handle(command, CancellationToken.None);

        // Idempotent outbox key + terms link in the body.
        _outbox.Verify(o => o.EnqueueEmailAsync(
            $"quote-email:{quote.Id}:buyer@acme.test",
            It.Is<EmailMessage>(m =>
                m.To == "buyer@acme.test"
                && m.HtmlBody.Contains("https://forge.example.com/api/v1/public/terms/")
                && m.HtmlBody.Contains("Thanks!")
                && m.HtmlBody.Contains("Standard Terms")
                && m.Attachments != null && m.Attachments.Count == 1),
            "Quote",
            quote.Id,
            It.IsAny<CancellationToken>()), Times.Once);

        // Immutable snapshot row.
        var snapshot = _db.QuoteTermsSnapshots.Single();
        snapshot.QuoteId.Should().Be(quote.Id);
        snapshot.SentTo.Should().Be("buyer@acme.test");
        snapshot.CompiledHtml.Should().Contain("Standard Terms");
        snapshot.CompiledSource.Should().Contain("\"version\":1");
        snapshot.AccessToken.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");

        // Status flip delegated to the existing SendQuote logic.
        _mediator.Verify(m => m.Send(
            It.Is<SendQuoteCommand>(c => c.Id == quote.Id),
            It.IsAny<CancellationToken>()), Times.Once);

        // Transactional entity: activity logged on the quote itself (the
        // auto-captured generic "created" rows from AppDbContext also exist).
        var log = _db.ActivityLogs.Single(l => l.Action == "quote-email-sent");
        log.EntityType.Should().Be("Quote");
        log.EntityId.Should().Be(quote.Id);
    }

    [Fact]
    public async Task Handle_AlreadySentQuote_Resends_WithoutSendQuoteCommand_AndRefreshesSentDate()
    {
        var quote = await SeedQuoteAsync(QuoteStatus.Sent);
        var command = new SendQuoteEmailCommand(quote.Id, "buyer@acme.test", null, "https://forge.example.com");

        await _handler.Handle(command, CancellationToken.None);

        _outbox.Verify(o => o.EnqueueEmailAsync(
            It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<string?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(
            It.IsAny<SendQuoteCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        quote.SentDate.Should().NotBeNull();
    }

    [Theory]
    [InlineData(QuoteStatus.Accepted)]
    [InlineData(QuoteStatus.Declined)]
    [InlineData(QuoteStatus.ConvertedToOrder)]
    public async Task Handle_NonDraftNonSentQuote_Throws(QuoteStatus status)
    {
        var quote = await SeedQuoteAsync(status);
        var act = () => _handler.Handle(
            new SendQuoteEmailCommand(quote.Id, "buyer@acme.test", null, "https://forge.example.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_UnknownQuote_ThrowsKeyNotFound()
    {
        var act = () => _handler.Handle(
            new SendQuoteEmailCommand(999, "a@b.test", null, "https://forge.example.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void GenerateAccessToken_IsUrlSafe_43Chars_AndUnique()
    {
        var tokens = Enumerable.Range(0, 200)
            .Select(_ => SendQuoteEmailHandler.GenerateAccessToken())
            .ToList();

        tokens.Should().OnlyHaveUniqueItems();
        tokens.Should().AllSatisfy(t =>
        {
            t.Length.Should().Be(43); // 32 bytes → 43 url-safe base64 chars (fits 64-char column)
            Regex.IsMatch(t, "^[A-Za-z0-9_-]+$").Should().BeTrue();
        });
    }

    [Fact]
    public void Validator_RejectsInvalidRecipientEmail()
    {
        var validator = new SendQuoteEmailValidator();

        validator.Validate(new SendQuoteEmailCommand(1, "not-an-email", null, "https://x"))
            .IsValid.Should().BeFalse();
        validator.Validate(new SendQuoteEmailCommand(1, "", null, "https://x"))
            .IsValid.Should().BeFalse();
        validator.Validate(new SendQuoteEmailCommand(1, "buyer@acme.test", null, "https://x"))
            .IsValid.Should().BeTrue();
    }
}
