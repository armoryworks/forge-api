using FluentAssertions;
using MediatR;
using Moq;

using Forge.Api.Features.Invoices;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.SalesOrders;

// F-033: source-state whitelist for CancelSalesOrder
// Whitelist per AUDIT.md: {Draft, Confirmed, PartiallyShipped}
// Blocked: InProduction, Cancelled (re-cancel), Shipped, Completed
public class CancelSalesOrderHandlerTests
{
    private readonly Mock<ISalesOrderRepository> _repo = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IClock> _clock = new();
    private readonly CancelSalesOrderHandler _handler;

    public CancelSalesOrderHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _mediator.Setup(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceListItemModel)null!);
        _handler = new CancelSalesOrderHandler(_repo.Object, _mediator.Object, _clock.Object);
    }

    private SalesOrder OrderInStatus(SalesOrderStatus status) =>
        new() { Id = 1, Status = status, CustomerId = 7, OrderNumber = "SO-1" };

    // ── happy path: whitelist states ──────────────────────────────────────────

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.PartiallyShipped)]
    public async Task Handle_WhitelistStatus_SetsCancelled(SalesOrderStatus status)
    {
        var order = OrderInStatus(status);
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        await _handler.Handle(new CancelSalesOrderCommand(1), CancellationToken.None);

        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // No fee → no invoice.
        _mediator.Verify(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── late-cancellation fee ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithFee_RecordsFeeAndInvoicesIt()
    {
        var order = OrderInStatus(SalesOrderStatus.Confirmed);
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        await _handler.Handle(new CancelSalesOrderCommand(1, 250m, "Late cancel < 48h"), CancellationToken.None);

        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        order.CancellationFeeAmount.Should().Be(250m);
        order.CancellationFeeReason.Should().Be("Late cancel < 48h");
        _mediator.Verify(m => m.Send(
            It.Is<CreateInvoiceCommand>(c => c.CustomerId == 7 && c.SalesOrderId == 1
                && c.Lines.Count == 1 && c.Lines[0].UnitPrice == 250m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ZeroFee_DoesNotInvoice()
    {
        var order = OrderInStatus(SalesOrderStatus.Confirmed);
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        await _handler.Handle(new CancelSalesOrderCommand(1, 0m, "no fee"), CancellationToken.None);

        order.CancellationFeeAmount.Should().BeNull();
        _mediator.Verify(m => m.Send(It.IsAny<CreateInvoiceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── F-033: blocked states ─────────────────────────────────────────────────

    [Theory]
    [InlineData(SalesOrderStatus.InProduction)]   // material committed to production
    [InlineData(SalesOrderStatus.Cancelled)]      // F-033: re-cancel is a silent duplicate — blocked
    [InlineData(SalesOrderStatus.Shipped)]
    [InlineData(SalesOrderStatus.Completed)]
    public async Task Handle_BlockedStatus_ThrowsInvalidOperation(SalesOrderStatus status)
    {
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(OrderInStatus(status));

        var act = () => _handler.Handle(new CancelSalesOrderCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Allowed: Draft, Confirmed, PartiallyShipped*");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ThrowsKeyNotFound()
    {
        _repo.Setup(r => r.FindAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((SalesOrder?)null);

        var act = () => _handler.Handle(new CancelSalesOrderCommand(99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
