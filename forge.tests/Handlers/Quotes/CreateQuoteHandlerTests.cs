using System.Security.Claims;

using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Quotes;

public class CreateQuoteHandlerTests
{
    private readonly Mock<IQuoteRepository> _quoteRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly CreateQuoteHandler _handler;

    private readonly Faker _faker = new();

    public CreateQuoteHandlerTests()
    {
        // WU-09 regression: passthrough default returns an active Part so the
        // active-check on PartId-bearing lines does not throw. Tests that
        // exercise part-active behavior override per-test.
        _partRepo
            .Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Part { Id = id, Status = PartStatus.Active });

        _handler = new CreateQuoteHandler(_quoteRepo.Object, _customerRepo.Object, _partRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesQuoteWithLines()
    {
        // Arrange
        var customerId = _faker.Random.Int(1, 100);
        var customerName = _faker.Company.CompanyName();
        var quoteNumber = $"QUO-{_faker.Random.Int(1000, 9999)}";
        var expirationDate = DateTime.UtcNow.AddDays(30);

        var customer = new Customer { Id = customerId, Name = customerName };
        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteNumber);

        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Widget A", 10, 25.50m, null),
            new(1, "Widget B", 5, 100.00m, "Rush order"),
        };

        var command = new CreateQuoteCommand(
            customerId, null, expirationDate, "Test quote", 0.08m, lines);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.QuoteNumber.Should().Be(quoteNumber);
        result.CustomerId.Should().Be(customerId);
        result.CustomerName.Should().Be(customerName);
        result.Status.Should().Be(QuoteStatus.Draft.ToString());
        result.LineCount.Should().Be(2);
        result.Total.Should().Be(10 * 25.50m + 5 * 100.00m); // 255 + 500 = 755
        result.ExpirationDate.Should().Be(expirationDate);

        _quoteRepo.Verify(r => r.AddAsync(It.Is<Quote>(q =>
            q.QuoteNumber == quoteNumber &&
            q.CustomerId == customerId &&
            q.ExpirationDate == expirationDate &&
            q.Notes == "Test quote" &&
            q.TaxRate == 0.08m &&
            q.Lines.Count == 2
        ), It.IsAny<CancellationToken>()), Times.Once);

        _quoteRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var customerId = 999;

        _customerRepo.Setup(r => r.FindAsync(customerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Widget", 1, 10.00m, null),
        };

        var command = new CreateQuoteCommand(customerId, null, null, null, 0m, lines);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*Customer {customerId}*");
    }

    [Fact]
    public async Task Handle_AssignsSequentialLineNumbers()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Test Corp" };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");

        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Line A", 1, 10m, null),
            new(null, "Line B", 2, 20m, null),
            new(null, "Line C", 3, 30m, null),
        };

        var command = new CreateQuoteCommand(1, null, null, null, 0m, lines);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _quoteRepo.Verify(r => r.AddAsync(It.Is<Quote>(q =>
            q.Lines.Count == 3 &&
            q.Lines.Any(l => l.LineNumber == 1 && l.Description == "Line A") &&
            q.Lines.Any(l => l.LineNumber == 2 && l.Description == "Line B") &&
            q.Lines.Any(l => l.LineNumber == 3 && l.Description == "Line C")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CalculatesTotalCorrectly()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Acme" };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");

        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Part A", 3, 100.00m, null),  // 300
            new(null, "Part B", 2, 50.00m, null),    // 100
        };

        var command = new CreateQuoteCommand(1, null, null, null, 0m, lines);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Total.Should().Be(400.00m);
    }

    [Fact]
    public async Task Handle_SingleLine_WorksCorrectly()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Solo Corp" };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0042");

        var lines = new List<CreateQuoteLineModel>
        {
            new(5, "Custom Widget", 100, 7.50m, "Bulk order"),
        };

        var command = new CreateQuoteCommand(1, null, null, null, 0.10m, lines);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.QuoteNumber.Should().Be("QUO-0042");
        result.LineCount.Should().Be(1);
        result.Total.Should().Be(750.00m); // 100 * 7.50

        _quoteRepo.Verify(r => r.AddAsync(It.Is<Quote>(q =>
            q.Lines.First().PartId == 5 &&
            q.Lines.First().Notes == "Bulk order"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsShippingAddressId()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Test" };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");

        var shippingAddressId = 42;
        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Item", 1, 10m, null),
        };

        var command = new CreateQuoteCommand(1, shippingAddressId, null, null, 0m, lines);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _quoteRepo.Verify(r => r.AddAsync(It.Is<Quote>(q =>
            q.ShippingAddressId == shippingAddressId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultStatus_IsDraft()
    {
        // Arrange
        var customer = new Customer { Id = 1, Name = "Test" };
        _customerRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customer);
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");

        var lines = new List<CreateQuoteLineModel>
        {
            new(null, "Widget", 1, 10m, null),
        };

        var command = new CreateQuoteCommand(1, null, null, null, 0m, lines);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(QuoteStatus.Draft.ToString());
    }

    [Theory]
    [InlineData(null, 9)]
    [InlineData(31, 31)]
    public async Task The_quote_is_owned_by_the_supplied_rep_or_else_the_creating_user(int? supplied, int expected)
    {
        _customerRepo.Setup(r => r.FindAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = 4, Name = "Owner Co" });
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("QT-00009");
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "9")], "test")),
        });
        var handler = new CreateQuoteHandler(_quoteRepo.Object, _customerRepo.Object, _partRepo.Object, httpContext: http.Object);

        await handler.Handle(new CreateQuoteCommand(
            4, null, null, null, 0m, [new CreateQuoteLineModel(null, "Setup", 1, 100m, null)], AssignedToId: supplied),
            CancellationToken.None);

        _quoteRepo.Verify(r => r.AddAsync(It.Is<Quote>(q => q.AssignedToId == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    public async Task A_supplied_rep_who_is_unknown_or_inactive_is_refused(int supplied, bool exists)
    {
        var db = Forge.Tests.Helpers.TestDbContextFactory.Create();
        if (exists)
            db.Users.Add(new Forge.Data.Context.ApplicationUser { Id = supplied, UserName = "former.rep", FirstName = "Former", LastName = "Rep", IsActive = false });
        await db.SaveChangesAsync();
        _customerRepo.Setup(r => r.FindAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = 4, Name = "Owner Co" });
        var handler = new CreateQuoteHandler(_quoteRepo.Object, _customerRepo.Object, _partRepo.Object, db: db);

        var act = () => handler.Handle(new CreateQuoteCommand(
            4, null, null, null, 0m, [new CreateQuoteLineModel(null, "Setup", 1, 100m, null)], AssignedToId: supplied),
            CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        _quoteRepo.Verify(r => r.AddAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
