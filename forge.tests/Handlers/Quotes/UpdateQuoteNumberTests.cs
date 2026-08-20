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
/// Editable quote-number behavior on UpdateQuote — mirrors the Part rename tests
/// (setting gate → uniqueness → registry rename) plus the Draft-only lifecycle
/// gate.
/// </summary>
public class UpdateQuoteNumberTests
{
    private readonly Mock<IQuoteRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateQuoteHandler _handler;

    public UpdateQuoteNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _handler = new UpdateQuoteHandler(
            _repo.Object,
            _settings.Object,
            _identifiers.Object,
            _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("quotes.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "quotes.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(Quote quote) =>
        _repo.Setup(r => r.FindAsync(quote.Id, It.IsAny<CancellationToken>())).ReturnsAsync(quote);

    private static UpdateQuoteCommand WithQuoteNumber(int id, string number) =>
        new(id, null, null, null, null, QuoteNumber: number);

    [Fact]
    public async Task Renames_the_quote_number_when_manual_numbers_allowed_and_unique()
    {
        var quote = new Quote { Id = 1, QuoteNumber = "QT-00001", Status = QuoteStatus.Draft };
        SetupRepoForUpdate(quote);
        AllowManualNumbers(true);
        _repo.Setup(r => r.QuoteNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithQuoteNumber(1, "ACME-42"), CancellationToken.None);

        quote.QuoteNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Quote, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_quote_number()
    {
        var quote = new Quote { Id = 1, QuoteNumber = "QT-00001", Status = QuoteStatus.Draft };
        SetupRepoForUpdate(quote);
        AllowManualNumbers(true);
        _repo.Setup(r => r.QuoteNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithQuoteNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        quote.QuoteNumber.Should().Be("QT-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var quote = new Quote { Id = 1, QuoteNumber = "QT-00001", Status = QuoteStatus.Draft };
        SetupRepoForUpdate(quote);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithQuoteNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        quote.QuoteNumber.Should().Be("QT-00001");
    }

    [Fact]
    public async Task Rejects_a_number_change_once_the_quote_is_past_Draft()
    {
        // A Sent quote is past Draft — the number's lifecycle gate rejects the
        // change with the number-specific message (evaluated before the general
        // Draft-only update guard).
        var quote = new Quote { Id = 1, QuoteNumber = "QT-00001", Status = QuoteStatus.Sent };
        SetupRepoForUpdate(quote);
        AllowManualNumbers(true);
        _repo.Setup(r => r.QuoteNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(WithQuoteNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("only be changed while it is Draft");
        quote.QuoteNumber.Should().Be("QT-00001");
        _identifiers.Verify(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
