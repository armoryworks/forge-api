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

    [Fact]
    public async Task Handle_NonExistentEstimate_ThrowsKeyNotFoundException()
    {
        var command = new ConvertEstimateToQuoteCommand(99999);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
