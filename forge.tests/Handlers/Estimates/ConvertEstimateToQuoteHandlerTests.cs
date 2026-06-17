using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.Estimates;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Estimates;

public class ConvertEstimateToQuoteHandlerTests
{
    private readonly Mock<IQuoteRepository> _quoteRepo = new();
    private readonly ConvertEstimateToQuoteHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public ConvertEstimateToQuoteHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _quoteRepo.Setup(r => r.GenerateNextQuoteNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("QUO-0001");
        _handler = new ConvertEstimateToQuoteHandler(_db, _quoteRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidEstimate_CreatesQuoteWithSourceLink()
    {
        // Arrange
        var customer = new Customer { Name = _faker.Company.CompanyName() };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var estimate = new Quote
        {
            Type = QuoteType.Estimate,
            Title = "Test Estimate",
            CustomerId = customer.Id,
            EstimatedAmount = 3000m,
            Status = QuoteStatus.Sent,
        };
        _db.Quotes.Add(estimate);
        await _db.SaveChangesAsync();

        var command = new ConvertEstimateToQuoteCommand(estimate.Id);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // Original estimate should be marked as converted
        var updatedEstimate = _db.Quotes.First(q => q.Id == estimate.Id);
        updatedEstimate.Status.Should().Be(QuoteStatus.ConvertedToQuote);

        // New quote should link back via SourceEstimateId
        var newQuote = _db.Quotes.First(q => q.Id == result.Id);
        newQuote.Type.Should().Be(QuoteType.Quote);
        newQuote.SourceEstimateId.Should().Be(estimate.Id);
    }

    [Fact] // #24 — line items (incl. lump-sum, PartId == null) must carry into the new quote.
    public async Task Handle_EstimateWithLines_CopiesLinesIntoQuote()
    {
        var customer = new Customer { Name = _faker.Company.CompanyName() };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var part = new Part { PartNumber = "P-24", Name = "Widget" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        var estimate = new Quote
        {
            Type = QuoteType.Estimate,
            Title = "Itemized Estimate",
            CustomerId = customer.Id,
            Status = QuoteStatus.Sent,
            Lines =
            {
                new QuoteLine { PartId = part.Id, Description = "Catalog line", Quantity = 2m, UnitPrice = 10m, LineNumber = 1 },
                new QuoteLine { PartId = null, Description = "Lump-sum line", Quantity = 1m, UnitPrice = 250m, LineNumber = 2 },
            },
        };
        _db.Quotes.Add(estimate);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ConvertEstimateToQuoteCommand(estimate.Id), CancellationToken.None);

        result.LineCount.Should().Be(2, "both estimate lines must transition into the quote");
        result.Total.Should().Be(270m, "2×10 + 1×250");

        var copied = _db.QuoteLines.Where(l => l.QuoteId == result.Id).OrderBy(l => l.LineNumber).ToList();
        copied.Should().HaveCount(2);
        copied[0].PartId.Should().Be(part.Id);
        copied[1].PartId.Should().BeNull("lump-sum lines copy as-is for resolution in the quote editor");
        copied[1].Description.Should().Be("Lump-sum line");
    }

    [Fact]
    public async Task Handle_NonExistentEstimate_ThrowsKeyNotFoundException()
    {
        var command = new ConvertEstimateToQuoteCommand(99999);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
