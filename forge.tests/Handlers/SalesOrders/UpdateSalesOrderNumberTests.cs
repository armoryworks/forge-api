using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// Editable order-number behavior on UpdateSalesOrder — mirrors the Part rename
/// tests (setting gate → uniqueness → registry rename) plus the Draft-only
/// lifecycle gate.
/// </summary>
public class UpdateSalesOrderNumberTests
{
    private readonly Mock<ISalesOrderRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateSalesOrderHandler _handler;

    public UpdateSalesOrderNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _handler = new UpdateSalesOrderHandler(
            _repo.Object,
            _settings.Object,
            _identifiers.Object,
            _db,
            Mock.Of<IMediator>(),
            Mock.Of<IHttpContextAccessor>());
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("sales_orders.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "sales_orders.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoForUpdate(SalesOrder order) =>
        _repo.Setup(r => r.FindAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);

    private static UpdateSalesOrderCommand WithOrderNumber(int id, string number) =>
        new(id, null, null, null, null, null, null, null, OrderNumber: number);

    [Fact]
    public async Task Renames_the_order_number_when_manual_numbers_allowed_and_unique()
    {
        var order = new SalesOrder { Id = 1, OrderNumber = "SO-00001", Status = SalesOrderStatus.Draft };
        SetupRepoForUpdate(order);
        AllowManualNumbers(true);
        _repo.Setup(r => r.OrderNumberExistsAsync("ACME-42", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(WithOrderNumber(1, "ACME-42"), CancellationToken.None);

        order.OrderNumber.Should().Be("ACME-42");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.SalesOrder, 1, "ACME-42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_order_number()
    {
        var order = new SalesOrder { Id = 1, OrderNumber = "SO-00001", Status = SalesOrderStatus.Draft };
        SetupRepoForUpdate(order);
        AllowManualNumbers(true);
        _repo.Setup(r => r.OrderNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(WithOrderNumber(1, "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        order.OrderNumber.Should().Be("SO-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var order = new SalesOrder { Id = 1, OrderNumber = "SO-00001", Status = SalesOrderStatus.Draft };
        SetupRepoForUpdate(order);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(WithOrderNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        order.OrderNumber.Should().Be("SO-00001");
    }

    [Fact]
    public async Task Rejects_a_number_change_once_the_order_is_past_Draft()
    {
        // Confirmed passes the general Draft-or-Confirmed update guard, so this
        // specifically exercises the number's Draft-only lifecycle gate.
        var order = new SalesOrder { Id = 1, OrderNumber = "SO-00001", Status = SalesOrderStatus.Confirmed };
        SetupRepoForUpdate(order);
        AllowManualNumbers(true);
        _repo.Setup(r => r.OrderNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(WithOrderNumber(1, "ACME-42"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("only be changed while it is Draft");
        order.OrderNumber.Should().Be("SO-00001");
        _identifiers.Verify(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
