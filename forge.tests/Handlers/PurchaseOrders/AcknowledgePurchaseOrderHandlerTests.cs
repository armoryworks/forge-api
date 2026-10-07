using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class AcknowledgePurchaseOrderHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct15 = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly AcknowledgePurchaseOrderHandler _handler;

    public AcknowledgePurchaseOrderHandlerTests()
    {
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        _handler = new AcknowledgePurchaseOrderHandler(_repo.Object, clock.Object, _db);
    }

    private PurchaseOrder Po(PurchaseOrderStatus status, DateTimeOffset? expected = null)
    {
        var po = new PurchaseOrder { Id = 7, PONumber = "PO-00007", Status = status, ExpectedDeliveryDate = expected };
        _repo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        return po;
    }

    [Fact]
    public async Task Acknowledging_without_a_promised_date_is_logged_and_stamped_from_the_clock()
    {
        var po = Po(PurchaseOrderStatus.Submitted, Oct1);

        await _handler.Handle(new AcknowledgePurchaseOrderCommand(7, null), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Acknowledged);
        po.AcknowledgedDate.Should().Be(Now);
        po.ExpectedDeliveryDate.Should().Be(Oct1);
        _db.ActivityLogs.Local.Should().ContainSingle(a =>
            a.EntityType == "PurchaseOrder" && a.EntityId == 7 && a.Action == "acknowledged"
            && a.Description == "Acknowledged");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_promised_date_replaces_the_expected_date_and_is_logged()
    {
        var po = Po(PurchaseOrderStatus.Submitted, Oct1);

        await _handler.Handle(new AcknowledgePurchaseOrderCommand(7, Oct15), CancellationToken.None);

        po.ExpectedDeliveryDate.Should().Be(Oct15);
        _db.ActivityLogs.Local.Should().ContainSingle(a =>
            a.Description == "Acknowledged; vendor promised 10/15/2026 (was 10/01/2026)");
    }

    [Fact]
    public async Task A_first_promised_date_is_logged_without_a_previous_one()
    {
        Po(PurchaseOrderStatus.Submitted);

        await _handler.Handle(new AcknowledgePurchaseOrderCommand(7, Oct15), CancellationToken.None);

        _db.ActivityLogs.Local.Should().ContainSingle(a => a.Description == "Acknowledged; vendor promised 10/15/2026");
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Acknowledged)]
    public async Task Only_submitted_orders_can_be_acknowledged(PurchaseOrderStatus status)
    {
        Po(status);

        var act = () => _handler.Handle(new AcknowledgePurchaseOrderCommand(7, Oct15), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.ActivityLogs.Local.Should().BeEmpty();
    }
}
