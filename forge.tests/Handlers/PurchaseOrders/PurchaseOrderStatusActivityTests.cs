using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class PurchaseOrderStatusActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 15, 0, TimeSpan.Zero);

    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public PurchaseOrderStatusActivityTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _db.CurrentUserId = 42;
    }

    private PurchaseOrder GivenPo(PurchaseOrderStatus status, params (decimal ordered, decimal received)[] lines)
    {
        var po = new PurchaseOrder { Id = 9, PONumber = "PO-00009", Status = status, QuoteCurrency = "USD" };
        var lineId = 1;
        foreach (var (ordered, received) in lines)
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = lineId++, PurchaseOrderId = 9, Description = "Bar", OrderedQuantity = ordered,
                ReceivedQuantity = received, UnitPrice = 2m,
            });
        _repo.Setup(r => r.FindAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        _repo.Setup(r => r.FindWithDetailsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(po);
        return po;
    }

    private ActivityLog SingleRow() =>
        _db.ActivityLogs.Local.Should().ContainSingle(a => a.EntityType == "PurchaseOrder" && a.EntityId == 9).Subject;

    [Fact]
    public async Task Submitting_logs_who_submitted_and_stamps_the_date_from_the_clock()
    {
        var po = GivenPo(PurchaseOrderStatus.Draft, (10m, 0m));
        var currency = new Mock<ICurrencyService>();
        currency.Setup(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>())).ReturnsAsync("USD");
        var handler = new SubmitPurchaseOrderHandler(
            _repo.Object, new Mock<IApprovalService>().Object, currency.Object, _httpContext.Object, _clock.Object, _db);

        await handler.Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        po.SubmittedDate.Should().Be(Now);
        var row = SingleRow();
        row.Action.Should().Be("submitted");
        row.Description.Should().Be("Submitted to vendor");
        row.UserId.Should().Be(42);
    }

    [Fact]
    public async Task A_rejected_submit_logs_nothing()
    {
        GivenPo(PurchaseOrderStatus.Submitted, (10m, 0m));
        var handler = new SubmitPurchaseOrderHandler(
            _repo.Object, new Mock<IApprovalService>().Object, new Mock<ICurrencyService>().Object,
            _httpContext.Object, _clock.Object, _db);

        var act = () => handler.Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.ActivityLogs.Local.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_logs_the_status_it_was_cancelled_from()
    {
        GivenPo(PurchaseOrderStatus.Acknowledged, (10m, 0m));

        await new CancelPurchaseOrderHandler(_repo.Object, _db)
            .Handle(new CancelPurchaseOrderCommand(9), CancellationToken.None);

        var row = SingleRow();
        row.Action.Should().Be("cancelled");
        row.Description.Should().Be("Cancelled (was Acknowledged)");
        row.UserId.Should().Be(42);
    }

    [Fact]
    public async Task Closing_a_received_order_is_logged()
    {
        var po = GivenPo(PurchaseOrderStatus.Received, (10m, 10m));

        await new ClosePurchaseOrderHandler(_repo.Object, _db)
            .Handle(new ClosePurchaseOrderCommand(9), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        var row = SingleRow();
        row.Action.Should().Be("closed");
        row.Description.Should().Be("Closed");
    }

    [Fact]
    public async Task Short_closing_logs_the_reason_and_the_cancelled_quantity()
    {
        var po = GivenPo(PurchaseOrderStatus.PartiallyReceived, (10m, 4m), (5m, 5m));
        var handler = new ShortClosePurchaseOrderHandler(
            _repo.Object, new Mock<IMediator>().Object, _httpContext.Object, _clock.Object, _db);

        await handler.Handle(new ShortClosePurchaseOrderCommand(9, "  Vendor discontinued the item  "), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Closed);
        po.ShortClosedAt.Should().Be(Now);
        var row = SingleRow();
        row.Action.Should().Be("short-closed");
        row.Description.Should().Be("Short-closed, 6 unreceived cancelled: Vendor discontinued the item");
    }

    [Fact]
    public async Task A_rejected_short_close_logs_nothing()
    {
        GivenPo(PurchaseOrderStatus.Received, (10m, 10m));
        var handler = new ShortClosePurchaseOrderHandler(
            _repo.Object, new Mock<IMediator>().Object, _httpContext.Object, _clock.Object, _db);

        var act = () => handler.Handle(new ShortClosePurchaseOrderCommand(9, "Reason"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.ActivityLogs.Local.Should().BeEmpty();
    }
}
