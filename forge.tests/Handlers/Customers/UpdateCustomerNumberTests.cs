using FluentAssertions;
using Moq;
using Forge.Api.Features.Customers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Customers;

public class UpdateCustomerNumberTests
{
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateCustomerHandler _handler;

    public UpdateCustomerNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());

        _handler = new UpdateCustomerHandler(
            _customerRepo.Object, _settings.Object, _identifiers.Object, _db, Mock.Of<IClock>());
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("customers.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "customers.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(Customer customer)
    {
        _customerRepo.Setup(r => r.FindAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _customerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    // Id + Name/CompanyName/Email/Phone/IsActive null, then the number.
    private static UpdateCustomerCommand WithCustomerNumber(int id, string number) =>
        new(id, null, null, null, null, null, CustomerNumber: number);

    [Fact]
    public async Task Renames_the_customer_number_when_manual_numbers_allowed_and_unique()
    {
        var customer = new Customer { Id = 1, Name = "Acme", CustomerNumber = "CUST-00001" };
        SetupRepoForUpdate(customer);
        AllowManualNumbers(true);
        _customerRepo.Setup(r => r.CustomerNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithCustomerNumber(1, "ACME-42"), CancellationToken.None);

        customer.CustomerNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Customer, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_customer_number()
    {
        var customer = new Customer { Id = 1, Name = "Acme", CustomerNumber = "CUST-00001" };
        SetupRepoForUpdate(customer);
        AllowManualNumbers(true);
        _customerRepo.Setup(r => r.CustomerNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithCustomerNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        customer.CustomerNumber.Should().Be("CUST-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var customer = new Customer { Id = 1, Name = "Acme", CustomerNumber = "CUST-00001" };
        SetupRepoForUpdate(customer);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithCustomerNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        customer.CustomerNumber.Should().Be("CUST-00001");
    }
}
