using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Inventory;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class PurchaseOrderApprovalGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 15, 0, TimeSpan.Zero);
    private const int BuyerId = 7;
    private const int ManagerId = 3;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IHttpContextAccessor> _httpContext = new();
    private readonly List<ApprovalCompletedEvent> _published = [];
    private readonly ApprovalService _approvals;
    private readonly PurchaseOrderRepository _repo;

    public PurchaseOrderApprovalGateTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(Now);
        _httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, BuyerId.ToString())], "test")),
        });
        _db.CurrentUserId = BuyerId;
        _approvals = new ApprovalService(_db, NullLogger<ApprovalService>.Instance);
        _repo = new PurchaseOrderRepository(_db);
    }

    private async Task<PurchaseOrder> SeedAsync(decimal? autoApproveBelow = null)
    {
        _db.Vendors.Add(new Vendor { Id = 4, CompanyName = "Mill Supply" });
        _db.ApprovalWorkflows.Add(new ApprovalWorkflow
        {
            Id = 1, Name = "PO over $5,000", EntityType = "PurchaseOrder",
            Steps =
            [
                new ApprovalStep
                {
                    StepNumber = 1, Name = "Manager", ApproverType = ApproverType.SpecificUser,
                    ApproverUserId = ManagerId, AutoApproveBelow = autoApproveBelow,
                },
            ],
        });
        var po = new PurchaseOrder
        {
            Id = 9, PONumber = "PO-00009", VendorId = 4, Status = PurchaseOrderStatus.Draft, QuoteCurrency = "USD",
            Lines = [new PurchaseOrderLine { Id = 1, Description = "Bar", OrderedQuantity = 1000m, UnitPrice = 6m }],
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return po;
    }

    private SubmitPurchaseOrderHandler SubmitHandler()
    {
        var currency = new Mock<ICurrencyService>();
        currency.Setup(c => c.GetBaseCurrencyAsync(It.IsAny<CancellationToken>())).ReturnsAsync("USD");
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<ApprovalCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalCompletedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        return new SubmitPurchaseOrderHandler(
            _repo, _approvals, currency.Object, _httpContext.Object, _clock.Object, _db, publisher.Object);
    }

    private Task CompleteAsync(bool approved, string? notes) =>
        new OnApprovalCompleted_UpdatePurchaseOrderStatus(_db, _clock.Object)
            .Handle(new ApprovalCompletedEvent("PurchaseOrder", 9, approved, ManagerId, notes), CancellationToken.None);

    [Fact]
    public async Task A_matching_workflow_holds_the_po_in_pending_approval_and_it_is_not_submitted()
    {
        var po = await SeedAsync();

        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.PendingApproval);
        po.SubmittedDate.Should().BeNull();
        _db.ApprovalRequests.Should().ContainSingle(r => r.EntityType == "PurchaseOrder" && r.EntityId == 9
            && r.Status == ApprovalRequestStatus.Pending);
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityId == 9 && a.Action == "approval-requested");
        _db.ActivityLogs.Should().NotContain(a => a.EntityId == 9 && a.Action == "submitted");
    }

    [Fact]
    public async Task Rejecting_the_approval_returns_the_po_to_draft_with_the_reason()
    {
        var po = await SeedAsync();
        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        await CompleteAsync(approved: false, notes: "Get a second quote");

        po.Status.Should().Be(PurchaseOrderStatus.Draft);
        po.SubmittedDate.Should().BeNull();
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityId == 9 && a.Action == "approval-rejected")
            .Which.Description.Should().Be("Approval rejected, returned to Draft: Get a second quote");
    }

    [Fact]
    public async Task Approving_submits_the_po_to_the_vendor()
    {
        var po = await SeedAsync();
        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);
        var request = _db.ApprovalRequests.Single(r => r.EntityType == "PurchaseOrder" && r.EntityId == 9);
        await _approvals.ApproveAsync(request.Id, ManagerId, null, CancellationToken.None);

        await CompleteAsync(approved: true, notes: null);

        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        po.SubmittedDate.Should().Be(Now);
        _db.ActivityLogs.Should().ContainSingle(a => a.EntityId == 9 && a.Action == "submitted");
    }

    [Fact]
    public async Task An_approval_does_not_release_the_po_while_its_own_request_is_still_open()
    {
        var po = await SeedAsync();
        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        await CompleteAsync(approved: true, notes: "Auto-approved (below threshold)");

        po.Status.Should().Be(PurchaseOrderStatus.PendingApproval);
        po.SubmittedDate.Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_a_pending_po_withdraws_its_open_approval_request()
    {
        await SeedAsync();
        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        await new CancelPurchaseOrderHandler(_repo, _db, _clock.Object).Handle(new CancelPurchaseOrderCommand(9), CancellationToken.None);

        _db.ApprovalRequests.Should().ContainSingle(r => r.EntityType == "PurchaseOrder" && r.EntityId == 9)
            .Which.Status.Should().Be(ApprovalRequestStatus.Cancelled);
    }

    [Fact]
    public async Task An_auto_approved_po_is_submitted_straight_away()
    {
        var po = await SeedAsync(autoApproveBelow: 1_000_000m);

        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        po.SubmittedDate.Should().Be(Now);
        _published.Should().ContainSingle(e => e.EntityType == "PurchaseOrder" && e.EntityId == 9 && e.Approved);
    }

    [Fact]
    public async Task Without_a_workflow_the_po_is_submitted_straight_away()
    {
        var po = await SeedAsync();
        _db.ApprovalWorkflows.RemoveRange(_db.ApprovalWorkflows);
        await _db.SaveChangesAsync();

        await SubmitHandler().Handle(new SubmitPurchaseOrderCommand(9), CancellationToken.None);

        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        _db.ApprovalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_completed_approval_does_not_touch_a_po_that_is_no_longer_pending()
    {
        var po = await SeedAsync();
        po.Status = PurchaseOrderStatus.Cancelled;
        await _db.SaveChangesAsync();

        await CompleteAsync(approved: true, notes: null);

        po.Status.Should().Be(PurchaseOrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.PendingApproval)]
    public async Task Receiving_items_is_refused_before_the_po_is_submitted(PurchaseOrderStatus status)
    {
        var po = await SeedAsync();
        po.Status = status;
        await _db.SaveChangesAsync();
        var handler = new ReceiveItemsHandler(_repo, _clock.Object, Mock.Of<IMediator>(), _httpContext.Object);

        var act = () => handler.Handle(
            new ReceiveItemsCommand(9, [new ReceiveLineModel(1, 5m, null, null)]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*status {status}*");
        po.Lines.Single().ReceivedQuantity.Should().Be(0m);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.PendingApproval)]
    public async Task Receiving_a_line_into_inventory_is_refused_before_the_po_is_submitted(PurchaseOrderStatus status)
    {
        var po = await SeedAsync();
        po.Status = status;
        await _db.SaveChangesAsync();
        var handler = new ReceivePurchaseOrderHandler(
            _repo, Mock.Of<IInventoryRepository>(), _httpContext.Object, _clock.Object);

        var act = () => handler.Handle(
            new ReceivePurchaseOrderCommand(new ReceivePurchaseOrderRequestModel(1, 5m, 2, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*status {status}*");
        _db.ReceivingRecords.Should().BeEmpty();
    }
}
