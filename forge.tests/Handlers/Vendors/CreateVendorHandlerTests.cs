using Bogus;
using FluentAssertions;
using Moq;

using Forge.Api.Features.Vendors;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Vendors;

public class CreateVendorHandlerTests
{
    private readonly Mock<IVendorRepository> _vendorRepo = new();
    private readonly CreateVendorHandler _handler;

    private readonly Faker _faker = new();

    public CreateVendorHandlerTests()
    {
        _handler = new CreateVendorHandler(_vendorRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesVendorAndReturnsListItem()
    {
        // Arrange
        var companyName = _faker.Company.CompanyName();
        var contactName = _faker.Name.FullName();
        var email = _faker.Internet.Email();
        var phone = _faker.Phone.PhoneNumber();
        var address = _faker.Address.StreetAddress();
        var city = _faker.Address.City();
        var state = _faker.Address.StateAbbr();
        var zip = _faker.Address.ZipCode();
        var country = "US";
        var paymentTerms = "Net 30";
        var notes = _faker.Lorem.Sentence();

        var command = new CreateVendorCommand(
            companyName, contactName, email, phone, address,
            city, state, zip, country, paymentTerms, notes);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CompanyName.Should().Be(companyName);
        result.ContactName.Should().Be(contactName);
        result.Email.Should().Be(email);
        result.Phone.Should().Be(phone);
        result.IsActive.Should().BeTrue();
        result.PoCount.Should().Be(0);

        _vendorRepo.Verify(r => r.AddAsync(It.Is<Vendor>(v =>
            v.CompanyName == companyName &&
            v.ContactName == contactName &&
            v.Email == email &&
            v.Phone == phone &&
            v.Address == address &&
            v.City == city &&
            v.State == state &&
            v.ZipCode == zip &&
            v.Country == country &&
            v.PaymentTerms == paymentTerms &&
            v.Notes == notes
        ), It.IsAny<CancellationToken>()), Times.Once);

        _vendorRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MinimalFields_CreatesVendorWithNulls()
    {
        // Arrange
        var companyName = _faker.Company.CompanyName();
        var command = new CreateVendorCommand(
            companyName, null, null, null, null,
            null, null, null, null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CompanyName.Should().Be(companyName);
        result.ContactName.Should().BeNull();
        result.Email.Should().BeNull();
        result.Phone.Should().BeNull();
        result.IsActive.Should().BeTrue();

        _vendorRepo.Verify(r => r.AddAsync(It.Is<Vendor>(v =>
            v.CompanyName == companyName &&
            v.ContactName == null &&
            v.Email == null &&
            v.Phone == null &&
            v.Address == null &&
            v.City == null &&
            v.State == null &&
            v.ZipCode == null &&
            v.Country == null &&
            v.PaymentTerms == null &&
            v.Notes == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewVendor_IsActiveByDefault()
    {
        // Arrange
        var command = new CreateVendorCommand(
            "Test Vendor", null, null, null, null,
            null, null, null, null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CallsSaveChangesAsync()
    {
        // Arrange
        var command = new CreateVendorCommand(
            "Test Vendor", null, null, null, null,
            null, null, null, null, null, null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _vendorRepo.Verify(r => r.AddAsync(It.IsAny<Vendor>(), It.IsAny<CancellationToken>()), Times.Once);
        _vendorRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsZeroOpenPoCount()
    {
        // Arrange — a new vendor should have no purchase orders
        var command = new CreateVendorCommand(
            _faker.Company.CompanyName(), null, null, null, null,
            null, null, null, null, null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.PoCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SetsAllAddressFields()
    {
        // Arrange
        var command = new CreateVendorCommand(
            "Parts Plus", "Bob Smith", "bob@partsplus.com", "555-9999",
            "123 Industrial Pkwy", "Detroit", "MI", "48201", "US",
            "Net 45", "Preferred supplier");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _vendorRepo.Verify(r => r.AddAsync(It.Is<Vendor>(v =>
            v.Address == "123 Industrial Pkwy" &&
            v.City == "Detroit" &&
            v.State == "MI" &&
            v.ZipCode == "48201" &&
            v.Country == "US"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
