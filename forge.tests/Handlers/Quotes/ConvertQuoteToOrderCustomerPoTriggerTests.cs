using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.CustomerPoDocuments;
using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Quotes;

/// <summary>
/// S4a — the convert-to-order handler mints the internal customer-PO record
/// if and only if the sales:auto_customer_po_enabled toggle is on. The
/// optional settings/mediator dependencies follow the S2 null-default
/// pattern, so the existing mock-repo constructions stay valid.
/// </summary>
public class ConvertQuoteToOrderCustomerPoTriggerTests
{
    private readonly Mock<IQuoteRepository> _quoteRepo = new();
    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task Convert_SettingEnabled_SendsGenerateCustomerPoCommand()
    {
        var quote = SetupAcceptedQuote();
        SetupSetting("True");
        _mediator.Setup(m => m.Send(
                It.IsAny<GenerateCustomerPoDocumentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerPoDocumentSummaryModel(
                1, 0, "CPO-00001", quote.Id, DateTimeOffset.UtcNow));

        var handler = BuildHandler();
        await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GenerateCustomerPoDocumentCommand>(c => c.GeneratedFromQuoteId == quote.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Convert_SettingDisabled_DoesNotGenerate()
    {
        var quote = SetupAcceptedQuote();
        SetupSetting("False");

        var handler = BuildHandler();
        await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.IsAny<GenerateCustomerPoDocumentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Convert_SettingAbsent_DefaultsOff_DoesNotGenerate()
    {
        var quote = SetupAcceptedQuote();
        _settings.Setup(s => s.FindByKeyAsync("sales:auto_customer_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemSetting?)null);

        var handler = BuildHandler();
        await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.IsAny<GenerateCustomerPoDocumentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Convert_WithoutOptionalDependencies_StillConverts()
    {
        // Back-compat: the pre-S4a constructor shape (repos only) must keep working.
        var quote = SetupAcceptedQuote();

        var handler = new ConvertQuoteToOrderHandler(_quoteRepo.Object, _orderRepo.Object);
        var result = await handler.Handle(new ConvertQuoteToOrderCommand(quote.Id), CancellationToken.None);

        result.Should().NotBeNull();
        quote.Status.Should().Be(QuoteStatus.ConvertedToOrder);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private ConvertQuoteToOrderHandler BuildHandler() =>
        new(_quoteRepo.Object, _orderRepo.Object, db: null,
            settings: _settings.Object, mediator: _mediator.Object);

    private void SetupSetting(string value) =>
        _settings.Setup(s => s.FindByKeyAsync("sales:auto_customer_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "sales:auto_customer_po_enabled", Value = value });

    private Quote SetupAcceptedQuote()
    {
        var quote = new Quote
        {
            Id = 11,
            CustomerId = 3,
            Status = QuoteStatus.Accepted,
            TaxRate = 0.05m,
            Customer = new Customer { Id = 3, Name = "Trigger Co" },
        };
        quote.Lines.Add(new QuoteLine
        {
            QuoteId = quote.Id,
            Description = "Widget",
            Quantity = 4,
            UnitPrice = 12m,
            LineNumber = 1,
        });

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quote.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);
        _quoteRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-90001");

        return quote;
    }
}
