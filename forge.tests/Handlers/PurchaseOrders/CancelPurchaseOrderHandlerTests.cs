using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.PurchaseOrders;

// F-033: source-state whitelist for CancelPurchaseOrder
// Whitelist per AUDIT.md: {Draft, Submitted, Acknowledged}
// Blocked: Cancelled (re-cancel), PartiallyReceived, Received, Closed
public class CancelPurchaseOrderHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly CancelPurchaseOrderHandler _handler;

    public CancelPurchaseOrderHandlerTests()
    {
        _handler = new CancelPurchaseOrderHandler(_repo.Object);
    }

    private PurchaseOrder PoInStatus(PurchaseOrderStatus status) => new() { Id = 1, Status = status };

    // ── happy path: whitelist states ──────────────────────────────────────────

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Acknowledged)]
    public async Task Handle_WhitelistStatus_SetsCancelled(PurchaseOrderStatus status)
    {
        var po = PoInStatus(status);
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(po);

        await _handler.Handle(new CancelPurchaseOrderCommand(1), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── F-033: blocked states ─────────────────────────────────────────────────

    [Theory]
    [InlineData(PurchaseOrderStatus.Cancelled)]       // F-033: re-cancel is a silent duplicate — blocked
    [InlineData(PurchaseOrderStatus.PartiallyReceived)] // items on dock — committed stock
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Closed)]
    public async Task Handle_BlockedStatus_ThrowsInvalidOperation(PurchaseOrderStatus status)
    {
        _repo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(PoInStatus(status));

        var act = () => _handler.Handle(new CancelPurchaseOrderCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Allowed: Draft, Submitted, Acknowledged*");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PoNotFound_ThrowsKeyNotFound()
    {
        _repo.Setup(r => r.FindAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((PurchaseOrder?)null);

        var act = () => _handler.Handle(new CancelPurchaseOrderCommand(99), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
