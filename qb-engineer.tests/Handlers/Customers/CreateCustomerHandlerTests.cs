using Bogus;
using FluentAssertions;
using Moq;

using QBEngineer.Api.Features.Customers;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.Customers;

public class CreateCustomerHandlerTests
{
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreateCustomerHandler _handler;

    private readonly Faker _faker = new();

    public CreateCustomerHandlerTests()
    {
        _handler = new CreateCustomerHandler(_customerRepo.Object, _db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesCustomerAndReturnsListItem()
    {
        // Arrange
        var name = _faker.Person.FullName;
        var companyName = _faker.Company.CompanyName();
        var email = _faker.Internet.Email();
        var phone = _faker.Phone.PhoneNumber();

        var command = new CreateCustomerCommand(name, companyName, email, phone);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(name);
        result.CompanyName.Should().Be(companyName);
        result.Email.Should().Be(email);
        result.Phone.Should().Be(phone);
        result.IsActive.Should().BeTrue();
        result.ContactCount.Should().Be(0);
        result.JobCount.Should().Be(0);

        _customerRepo.Verify(r => r.AddAsync(It.Is<Customer>(c =>
            c.Name == name &&
            c.CompanyName == companyName &&
            c.Email == email &&
            c.Phone == phone
        ), It.IsAny<CancellationToken>()), Times.Once);

        _customerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MinimalFields_CreatesCustomerWithNameOnly()
    {
        // Arrange
        var name = _faker.Person.FullName;
        var command = new CreateCustomerCommand(name, null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(name);
        result.CompanyName.Should().BeNull();
        result.Email.Should().BeNull();
        result.Phone.Should().BeNull();

        _customerRepo.Verify(r => r.AddAsync(It.Is<Customer>(c =>
            c.Name == name &&
            c.CompanyName == null &&
            c.Email == null &&
            c.Phone == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DefaultsIsActiveToTrue()
    {
        // Arrange
        var command = new CreateCustomerCommand("Test Customer", null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _customerRepo.Verify(r => r.AddAsync(It.Is<Customer>(c =>
            c.IsActive == true
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallsSaveChangesAfterAdd()
    {
        // Arrange
        var command = new CreateCustomerCommand("Test", "Test Co", null, null);
        var callOrder = new List<string>();

        _customerRepo.Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Add"))
            .Returns(Task.CompletedTask);

        _customerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Save"))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        callOrder.Should().ContainInOrder("Add", "Save");
    }

    [Fact]
    public async Task Handle_ReturnsZeroCountsForNewCustomer()
    {
        // Arrange
        var command = new CreateCustomerCommand("New Customer", null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.ContactCount.Should().Be(0);
        result.JobCount.Should().Be(0);
    }
}
