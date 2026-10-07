using FluentAssertions;
using FluentValidation;
using Moq;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.SalesOrders;

public class CreateSalesOrderAddressTests
{
    private const int CustomerId = 7;

    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IBarcodeService> _barcodeService = new();
    private readonly Mock<ICustomerAddressRepository> _addressRepo = new();
    private readonly CreateSalesOrderHandler _handler;

    private SalesOrder? _added;

    public CreateSalesOrderAddressTests()
    {
        _customerRepo.Setup(r => r.FindAsync(CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = CustomerId, Name = "Acme Fabrication" });
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-00001");
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback((SalesOrder so, CancellationToken _) => _added = so)
            .Returns(Task.CompletedTask);

        _handler = new CreateSalesOrderHandler(
            _orderRepo.Object, _customerRepo.Object, _partRepo.Object, _barcodeService.Object, _addressRepo.Object);
    }

    private void GivenAddresses(params CustomerAddressResponseModel[] addresses) =>
        _addressRepo.Setup(r => r.GetByCustomerAsync(CustomerId, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(addresses.ToList());

    private static CustomerAddressResponseModel Address(int id, string type, bool isDefault, bool isActive = true) =>
        new(id, $"Address {id}", type, "1 Main St", null, "Denver", "CO", "80202", "US", isDefault, isActive);

    private static CreateSalesOrderCommand Command(int? shippingAddressId = null, int? billingAddressId = null) =>
        new(CustomerId, null, shippingAddressId, billingAddressId, null, null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Widget", 1, 10m, null)]);

    [Fact]
    public async Task Handle_NoAddressesSupplied_DefaultsEachToTheMatchingDefaultAddress()
    {
        GivenAddresses(
            Address(1, "Shipping", isDefault: false),
            Address(2, "Shipping", isDefault: true),
            Address(3, "Billing", isDefault: true));

        await _handler.Handle(Command(), CancellationToken.None);

        _added!.ShippingAddressId.Should().Be(2);
        _added.BillingAddressId.Should().Be(3);
    }

    [Fact]
    public async Task Handle_OnlyABothDefault_UsesItForShipToAndBillTo()
    {
        GivenAddresses(Address(4, "Both", isDefault: true));

        await _handler.Handle(Command(), CancellationToken.None);

        _added!.ShippingAddressId.Should().Be(4);
        _added.BillingAddressId.Should().Be(4);
    }

    [Fact]
    public async Task Handle_TypedDefaultAndBothDefault_PrefersTheTypedDefault()
    {
        GivenAddresses(
            Address(5, "Both", isDefault: true),
            Address(6, "Shipping", isDefault: true));

        await _handler.Handle(Command(), CancellationToken.None);

        _added!.ShippingAddressId.Should().Be(6);
        _added.BillingAddressId.Should().Be(5);
    }

    [Fact]
    public async Task Handle_NoDefaultAddress_LeavesAddressesEmpty()
    {
        GivenAddresses(
            Address(8, "Shipping", isDefault: false),
            Address(9, "Billing", isDefault: true, isActive: false));

        await _handler.Handle(Command(), CancellationToken.None);

        _added!.ShippingAddressId.Should().BeNull();
        _added.BillingAddressId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SuppliedAddressOfTheCustomer_WinsOverTheDefault()
    {
        GivenAddresses(
            Address(2, "Shipping", isDefault: true),
            Address(3, "Billing", isDefault: true));
        _addressRepo.Setup(r => r.FindAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerAddress { Id = 10, CustomerId = CustomerId });

        await _handler.Handle(Command(shippingAddressId: 10), CancellationToken.None);

        _added!.ShippingAddressId.Should().Be(10);
        _added.BillingAddressId.Should().Be(3);
    }

    [Fact]
    public async Task Handle_SuppliedShippingAddressOfAnotherCustomer_IsRejected()
    {
        GivenAddresses();
        _addressRepo.Setup(r => r.FindAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerAddress { Id = 11, CustomerId = CustomerId + 1 });

        var act = () => _handler.Handle(Command(shippingAddressId: 11), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "shippingAddressId");
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SuppliedBillingAddressThatDoesNotExist_IsRejected()
    {
        GivenAddresses();
        _addressRepo.Setup(r => r.FindAsync(12, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerAddress?)null);

        var act = () => _handler.Handle(Command(billingAddressId: 12), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "billingAddressId");
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
