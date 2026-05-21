using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Quotes;

public class ConvertQuoteToOrderHandlerTests
{
    private readonly Mock<IQuoteRepository> _quoteRepo = new();
    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly ConvertQuoteToOrderHandler _handler;

    private readonly Faker _faker = new();

    public ConvertQuoteToOrderHandlerTests()
    {
        _handler = new ConvertQuoteToOrderHandler(_quoteRepo.Object, _orderRepo.Object);
    }

    [Fact]
    public async Task Handle_AcceptedQuote_ConvertsToSalesOrderAndCopiesLines()
    {
        // Arrange
        var quoteId = _faker.Random.Int(1, 100);
        var customerId = _faker.Random.Int(1, 50);
        var orderNumber = $"SO-{_faker.Random.Int(1000, 9999)}";

        var quote = new Quote
        {
            Id = quoteId,
            QuoteNumber = "QT-0001",
            CustomerId = customerId,
            Status = QuoteStatus.Accepted,
            TaxRate = 0.07m,
            ShippingAddressId = 5,
            Customer = new Customer { Id = customerId, Name = "Acme Corp" },
        };
        quote.Lines.Add(new QuoteLine
        {
            QuoteId = quoteId,
            Description = "Widget A",
            Quantity = 10,
            UnitPrice = 25m,
            LineNumber = 1,
            Notes = "Rush order",
        });
        quote.Lines.Add(new QuoteLine
        {
            QuoteId = quoteId,
            PartId = 42,
            Description = "Widget B",
            Quantity = 5,
            UnitPrice = 50m,
            LineNumber = 2,
        });

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderNumber);

        var command = new ConvertQuoteToOrderCommand(quoteId);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OrderNumber.Should().Be(orderNumber);
        result.CustomerId.Should().Be(customerId);
        result.CustomerName.Should().Be("Acme Corp");
        result.LineCount.Should().Be(2);

        // Total should be (10*25 + 5*50) = 500
        result.Total.Should().Be(500m);

        // Quote status should be updated
        quote.Status.Should().Be(QuoteStatus.ConvertedToOrder);

        _orderRepo.Verify(r => r.AddAsync(It.Is<SalesOrder>(o =>
            o.OrderNumber == orderNumber &&
            o.CustomerId == customerId &&
            o.QuoteId == quoteId &&
            o.ShippingAddressId == 5 &&
            o.TaxRate == 0.07m &&
            o.Lines.Count == 2
        ), It.IsAny<CancellationToken>()), Times.Once);

        _quoteRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CopiesLineDetailsFromQuoteToOrder()
    {
        // Arrange
        var quoteId = 1;
        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = 1,
            Status = QuoteStatus.Accepted,
            TaxRate = 0m,
            Customer = new Customer { Id = 1, Name = "Test" },
        };
        quote.Lines.Add(new QuoteLine
        {
            Description = "Custom Part",
            PartId = 99,
            Quantity = 3,
            UnitPrice = 100m,
            LineNumber = 1,
            Notes = "Special handling",
        });

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-0001");

        SalesOrder? capturedOrder = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback<SalesOrder, CancellationToken>((o, _) => capturedOrder = o);

        var command = new ConvertQuoteToOrderCommand(quoteId);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedOrder.Should().NotBeNull();
        var line = capturedOrder!.Lines.First();
        line.PartId.Should().Be(99);
        line.Description.Should().Be("Custom Part");
        line.Quantity.Should().Be(3);
        line.UnitPrice.Should().Be(100m);
        line.LineNumber.Should().Be(1);
        line.Notes.Should().Be("Special handling");
    }

    [Fact]
    public async Task Handle_QuoteNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var quoteId = _faker.Random.Int(1, 100);

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Quote?)null);

        var command = new ConvertQuoteToOrderCommand(quoteId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Quote {quoteId}*");
    }

    [Fact]
    public async Task Handle_NonAcceptedQuote_ThrowsInvalidOperationException()
    {
        // Arrange
        var quoteId = _faker.Random.Int(1, 100);
        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = 1,
            Status = QuoteStatus.Draft,
            Customer = new Customer { Id = 1, Name = "Test" },
        };

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);

        var command = new ConvertQuoteToOrderCommand(quoteId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Accepted*");
    }

    [Fact]
    public async Task Handle_AlreadyConvertedQuote_ThrowsInvalidOperationException()
    {
        // Arrange
        var quoteId = _faker.Random.Int(1, 100);
        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = 1,
            Status = QuoteStatus.Accepted,
            Customer = new Customer { Id = 1, Name = "Test" },
            SalesOrder = new SalesOrder { OrderNumber = "SO-0001", CustomerId = 1 },
        };

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);

        var command = new ConvertQuoteToOrderCommand(quoteId);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already been converted*");
    }

    // ── INV-SO1 / REQ-SO-01 ──────────────────────────────────────────────────

    [Fact]
    public async Task PriceLock_AllSoLinesPreserveExactQuoteUnitPrice_InvSo1()
    {
        // Asserts INV-SO1: every SO line's UnitPrice must equal the originating
        // quote line's UnitPrice exactly — no rounding, no override, no drift.
        var quoteId = _faker.Random.Int(1, 100);
        var quoteLines = Enumerable.Range(1, 5).Select(i => new QuoteLine
        {
            QuoteId = quoteId,
            Description = _faker.Commerce.ProductName(),
            Quantity = _faker.Random.Decimal(1m, 100m),
            UnitPrice = _faker.Random.Decimal(0.01m, 9999.99m),
            LineNumber = i,
        }).ToList();

        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = 1,
            Status = QuoteStatus.Accepted,
            TaxRate = 0.08m,
            Customer = new Customer { Id = 1, Name = "LockCo" },
        };
        foreach (var ql in quoteLines)
            quote.Lines.Add(ql);

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-LOCK-01");

        SalesOrder? captured = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback<SalesOrder, CancellationToken>((o, _) => captured = o);

        await _handler.Handle(new ConvertQuoteToOrderCommand(quoteId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Lines.Should().HaveCount(quoteLines.Count);

        // Every SO line must carry the exact UnitPrice from the corresponding quote line.
        var ordered = captured.Lines.OrderBy(l => l.LineNumber).ToList();
        for (var i = 0; i < quoteLines.Count; i++)
        {
            ordered[i].UnitPrice.Should().Be(quoteLines[i].UnitPrice,
                $"SO line {i + 1} must preserve the quote line UnitPrice (INV-SO1)");
        }
    }

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Declined)]
    [InlineData(QuoteStatus.Expired)]
    [InlineData(QuoteStatus.ConvertedToOrder)]
    public async Task NonAcceptedQuote_ThrowsInvalidOperationException_AllNonAcceptedStates(QuoteStatus status)
    {
        // Asserts that the guard on line 19-20 of ConvertQuoteToOrder.cs fires for
        // every non-Accepted status — not only Draft (REQ-SO-01 pre-condition).
        var quoteId = _faker.Random.Int(1, 100);
        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = 1,
            Status = status,
            Customer = new Customer { Id = 1, Name = "Test" },
        };

        _quoteRepo.Setup(r => r.FindWithDetailsAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);

        var act = () => _handler.Handle(new ConvertQuoteToOrderCommand(quoteId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Accepted*");
    }
}
