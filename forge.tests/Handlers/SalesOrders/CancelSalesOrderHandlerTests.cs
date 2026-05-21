using FluentAssertions;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.SalesOrders;

// F-033: source-state whitelist for CancelSalesOrder
// Whitelist per AUDIT.md: {Draft, Confirmed, PartiallyShipped}
// Blocked: InProduction, Cancelled (re-cancel), Shipped, Completed
public class CancelSalesOrderHandlerTests
{
    private readonly Mock<ISalesOrderRepository> _repo = new();
    private readonly CancelSalesOrderHandler _handler;

    public CancelSalesOrderHandlerTests()
    {
        _handler = new CancelSalesOrderHandler(_repo.Object);
    }

    private SalesOrder OrderInStatus(SalesOrderStatus status) => new() { Id = 1, Status = status };

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
