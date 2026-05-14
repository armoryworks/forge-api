using Bogus;
using FluentAssertions;
using Forge.Api.Features.Estimates;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Estimates;

public class CreateEstimateHandlerTests
{
    private readonly CreateEstimateHandler _handler;
    private readonly Data.Context.AppDbContext _db;
    private readonly Faker _faker = new();

    public CreateEstimateHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new CreateEstimateHandler(_db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesEstimateAsQuoteWithEstimateType()
    {
        // Arrange
        var customer = new Customer { Name = _faker.Company.CompanyName() };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var command = new CreateEstimateCommand(
            customer.Id, "Test Estimate", "Description", 5000m, null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("Test Estimate");
        result.CustomerId.Should().Be(customer.Id);

        var stored = _db.Quotes.First(q => q.Id == result.Id);
        stored.Type.Should().Be(QuoteType.Estimate);
        stored.EstimatedAmount.Should().Be(5000m);
    }

    [Fact]
    public async Task Handle_SetsStatusToDraft()
    {
        var customer = new Customer { Name = _faker.Company.CompanyName() };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var command = new CreateEstimateCommand(
            customer.Id, "Draft Estimate", null, 1000m, null, null, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Status.Should().Be("Draft");
    }
}
