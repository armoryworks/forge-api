using FluentAssertions;
using Moq;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.SalesOrders;

public class CreateSalesOrderCreditTermsTests
{
    private const int CustomerId = 8;

    private readonly Mock<ISalesOrderRepository> _orderRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<ICustomerAddressRepository> _addressRepo = new();
    private readonly CreateSalesOrderHandler _handler;

    private SalesOrder? _added;

    public CreateSalesOrderCreditTermsTests()
    {
        _customerRepo.Setup(r => r.FindAsync(CustomerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = CustomerId, Name = "Acme Fabrication", DefaultCreditTerms = CreditTerms.Net60 });
        _orderRepo.Setup(r => r.GenerateNextOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SO-00002");
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<SalesOrder>(), It.IsAny<CancellationToken>()))
            .Callback((SalesOrder so, CancellationToken _) => _added = so)
            .Returns(Task.CompletedTask);
        _addressRepo.Setup(r => r.GetByCustomerAsync(CustomerId, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync([]);

        _handler = new CreateSalesOrderHandler(
            _orderRepo.Object, _customerRepo.Object, Mock.Of<IPartRepository>(), Mock.Of<IBarcodeService>(), _addressRepo.Object);
    }

    private static CreateSalesOrderCommand Command(string? creditTerms) =>
        new(CustomerId, null, null, null, creditTerms, null, null, null, 0m,
            [new CreateSalesOrderLineModel(null, "Widget", 1, 10m, null)]);

    [Fact]
    public async Task Order_without_terms_starts_from_the_customer_default()
    {
        await _handler.Handle(Command(null), CancellationToken.None);

        _added!.CreditTerms.Should().Be(CreditTerms.Net60);
    }

    [Fact]
    public async Task Typed_terms_win_over_the_customer_default()
    {
        await _handler.Handle(Command("Net 15"), CancellationToken.None);

        _added!.CreditTerms.Should().Be(CreditTerms.Net15);
    }

    [Fact]
    public void Validator_rejects_unknown_terms()
    {
        new CreateSalesOrderValidator().Validate(Command("Net 31")).IsValid.Should().BeFalse();
        new UpdateSalesOrderValidator()
            .Validate(new UpdateSalesOrderCommand(1, null, null, "3", null, null, null, null))
            .IsValid.Should().BeFalse();
    }
}
