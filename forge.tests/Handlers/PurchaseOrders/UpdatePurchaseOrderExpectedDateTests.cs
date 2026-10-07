using FluentAssertions;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class UpdatePurchaseOrderExpectedDateTests
{
    private static readonly DateTimeOffset Oct1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Oct15 = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdatePurchaseOrderHandler _handler;

    public UpdatePurchaseOrderExpectedDateTests()
    {
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _handler = new UpdatePurchaseOrderHandler(
            _repo.Object, new Mock<ISystemSettingRepository>().Object, new Mock<IBusinessIdentifierService>().Object, _db);
    }

    private PurchaseOrder Po(PurchaseOrderStatus status, DateTimeOffset? expected = null, string? notes = null)
    {
        var po = new PurchaseOrder { Id = 7, PONumber = "PO-00007", Status = status, ExpectedDeliveryDate = expected, Notes = notes };
        _repo.Setup(r => r.FindAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        return po;
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Acknowledged)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived)]
    public async Task Expected_date_can_change_after_acknowledgement(PurchaseOrderStatus status)
    {
        var po = Po(status, Oct1);

        await _handler.Handle(new UpdatePurchaseOrderCommand(7, null, Oct15), CancellationToken.None);

        po.ExpectedDeliveryDate.Should().Be(Oct15);
        _db.ActivityLogs.Local.Should().ContainSingle(a =>
            a.EntityType == "PurchaseOrder" && a.EntityId == 7 && a.Action == "updated"
            && a.Description == "Changed expected delivery from 10/01/2026 to 10/15/2026");
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Setting_a_first_expected_date_is_logged()
    {
        Po(PurchaseOrderStatus.Acknowledged);

        await _handler.Handle(new UpdatePurchaseOrderCommand(7, null, Oct15), CancellationToken.None);

        _db.ActivityLogs.Local.Should().ContainSingle(a => a.Description == "Set expected delivery to 10/15/2026");
    }

    [Fact]
    public async Task Notes_stay_locked_after_acknowledgement()
    {
        var po = Po(PurchaseOrderStatus.Acknowledged, Oct1, "original");

        var act = () => _handler.Handle(new UpdatePurchaseOrderCommand(7, "changed", Oct15), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("expected delivery date");
        po.ExpectedDeliveryDate.Should().Be(Oct1);
        po.Notes.Should().Be("original");
        _db.ActivityLogs.Local.Should().BeEmpty();
    }

    [Fact]
    public async Task Unchanged_notes_sent_alongside_the_date_are_accepted_after_acknowledgement()
    {
        var po = Po(PurchaseOrderStatus.Acknowledged, Oct1, "original");

        await _handler.Handle(new UpdatePurchaseOrderCommand(7, "original", Oct15), CancellationToken.None);

        po.ExpectedDeliveryDate.Should().Be(Oct15);
    }

    [Fact]
    public async Task Landed_cost_fields_stay_locked_after_acknowledgement()
    {
        Po(PurchaseOrderStatus.Acknowledged, Oct1);

        var act = () => _handler.Handle(
            new UpdatePurchaseOrderCommand(7, null, Oct15, EstimatedFreight: 10m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task Expected_date_is_locked_once_the_po_is_done(PurchaseOrderStatus status)
    {
        var po = Po(status, Oct1);

        var act = () => _handler.Handle(new UpdatePurchaseOrderCommand(7, null, Oct15), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        po.ExpectedDeliveryDate.Should().Be(Oct1);
    }

    [Fact]
    public async Task A_multi_field_update_writes_one_rollup_row()
    {
        var po = Po(PurchaseOrderStatus.Submitted, Oct1, "old");

        await _handler.Handle(new UpdatePurchaseOrderCommand(7, "new", Oct15), CancellationToken.None);

        po.Notes.Should().Be("new");
        _db.ActivityLogs.Local.Should().ContainSingle(a => a.Description == "Updated 2 fields: notes, expectedDeliveryDate");
    }

    [Fact]
    public async Task A_no_op_update_writes_no_activity()
    {
        Po(PurchaseOrderStatus.Acknowledged, Oct15, "same");

        await _handler.Handle(new UpdatePurchaseOrderCommand(7, "same", Oct15), CancellationToken.None);

        _db.ActivityLogs.Local.Should().BeEmpty();
    }
}
