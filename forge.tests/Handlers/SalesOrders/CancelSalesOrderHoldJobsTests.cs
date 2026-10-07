using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Features.Jobs;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.StatusTracking;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class CancelSalesOrderHoldJobsTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IHubContext<BoardHub>> _boardHub = new();
    private readonly Mock<IHubContext<NotificationHub>> _notificationHub = new();
    private readonly Mock<IClientProxy> _boardProxy = new();
    private readonly List<string> _boardGroups = [];

    public CancelSalesOrderHoldJobsTests()
    {
        var addHold = new AddHoldHandler(
            _db,
            new StatusEntryRepository(_db),
            new ActivityLogRepository(_db),
            Mock.Of<IWorkCenterContext>(),
            Mock.Of<IHttpContextAccessor>());
        _mediator.Setup(m => m.Send(It.IsAny<AddHoldCommand>(), It.IsAny<CancellationToken>()))
            .Returns<AddHoldCommand, CancellationToken>((cmd, ct) => addHold.Handle(cmd, ct));
        _mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);

        var boardClients = new Mock<IHubClients>();
        boardClients.Setup(c => c.Group(It.IsAny<string>()))
            .Callback<string>(g => _boardGroups.Add(g))
            .Returns(_boardProxy.Object);
        _boardHub.SetupGet(h => h.Clients).Returns(boardClients.Object);

        var notificationClients = new Mock<IHubClients>();
        notificationClients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        _notificationHub.SetupGet(h => h.Clients).Returns(notificationClients.Object);
    }

    private OnSalesOrderCancelled_HoldJobs Handler() => new(
        _db, _mediator.Object, _boardHub.Object, _notificationHub.Object,
        NullLogger<OnSalesOrderCancelled_HoldJobs>.Instance);

    private async Task SeedAsync()
    {
        _db.Customers.Add(new Customer { Id = 1, Name = "Acme" });
        _db.SalesOrders.Add(new SalesOrder
        {
            Id = 501, OrderNumber = "SO-00501", CustomerId = 1, Status = SalesOrderStatus.Cancelled,
            Lines =
            {
                new SalesOrderLine { Id = 601, LineNumber = 1, Description = "Bracket", Quantity = 10m, UnitPrice = 1m },
                new SalesOrderLine { Id = 602, LineNumber = 2, Description = "Hinge", Quantity = 5m, ShippedQuantity = 2m, UnitPrice = 1m },
                new SalesOrderLine { Id = 603, LineNumber = 3, Description = "Pin", Quantity = 4m, ShippedQuantity = 4m, UnitPrice = 1m },
            },
        });
        _db.Jobs.AddRange(
            new Job { Id = 1, JobNumber = "J-1", Title = "Bracket", TrackTypeId = 7, CurrentStageId = 70, SalesOrderLineId = 601 },
            new Job { Id = 2, JobNumber = "J-2", Title = "Hinge", TrackTypeId = 7, CurrentStageId = 70, SalesOrderLineId = 602 },
            new Job
            {
                Id = 3, JobNumber = "J-3", Title = "Bracket rework", TrackTypeId = 7, CurrentStageId = 79,
                SalesOrderLineId = 601, CompletedDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new Job { Id = 4, JobNumber = "J-4", Title = "Pin", TrackTypeId = 7, CurrentStageId = 70, SalesOrderLineId = 603 },
            new Job { Id = 5, JobNumber = "J-5", Title = "Archived", TrackTypeId = 7, CurrentStageId = 70, SalesOrderLineId = 601, IsArchived = true });

        var manager = new ApplicationUser
        {
            Id = 42, UserName = "pm@forge.local", Email = "pm@forge.local",
            FirstName = "Pat", LastName = "Manager", Initials = "PM", AvatarColor = "#888",
        };
        _db.Users.Add(manager);
        _db.Roles.Add(new IdentityRole<int> { Id = 9, Name = "Manager", NormalizedName = "MANAGER" });
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = 42, RoleId = 9 });
        await _db.SaveChangesAsync();
    }

    private Task<List<StatusEntry>> ActiveJobHoldsAsync() =>
        _db.StatusEntries.Where(s => s.EntityType == "Job" && s.Category == "hold" && s.EndedAt == null).ToListAsync();

    [Fact]
    public async Task Cancel_places_a_reversible_hold_on_each_open_job_with_the_order_as_the_reason()
    {
        await SeedAsync();

        await Handler().Handle(new SalesOrderCancelledEvent(501), CancellationToken.None);

        var holds = await ActiveJobHoldsAsync();
        holds.Select(h => h.EntityId).Should().BeEquivalentTo([1, 2]);
        holds.Should().OnlyContain(h => h.StatusCode == "job_hold_customer"
                                        && h.Notes == "Sales order SO-00501 cancelled");
    }

    [Fact]
    public async Task Cancel_leaves_completed_archived_and_fully_shipped_jobs_alone()
    {
        await SeedAsync();

        await Handler().Handle(new SalesOrderCancelledEvent(501), CancellationToken.None);

        var heldIds = (await ActiveJobHoldsAsync()).Select(h => h.EntityId).ToList();
        heldIds.Should().NotContain([3, 4, 5]);
        (await _db.Jobs.FindAsync(3))!.IsArchived.Should().BeFalse();
        (await _db.Jobs.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Cancel_logs_the_hold_on_the_job_and_the_order()
    {
        await SeedAsync();

        await Handler().Handle(new SalesOrderCancelledEvent(501), CancellationToken.None);

        (await _db.JobActivityLogs.Where(l => l.FieldName == "Hold").Select(l => l.JobId).ToListAsync())
            .Should().BeEquivalentTo([1, 2]);
        (await _db.ActivityLogs.SingleAsync(l => l.EntityType == "SalesOrder" && l.Action == "jobs-held"))
            .EntityId.Should().Be(501);
    }

    [Fact]
    public async Task Cancel_notifies_the_production_manager_and_updates_the_board()
    {
        await SeedAsync();

        await Handler().Handle(new SalesOrderCancelledEvent(501), CancellationToken.None);

        var notification = await _db.Notifications.SingleAsync();
        notification.UserId.Should().Be(42);
        notification.Type.Should().Be("sales_order_cancelled");
        notification.Message.Should().Contain("SO-00501").And.Contain("J-1").And.Contain("J-2");
        _boardGroups.Should().Contain(["board:7", "job:1", "job:2"]);
        _boardProxy.Verify(p => p.SendCoreAsync("jobUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    [Fact]
    public async Task A_job_already_on_customer_hold_is_not_held_twice()
    {
        await SeedAsync();
        _db.StatusEntries.Add(new StatusEntry
        {
            EntityType = "Job", EntityId = 1, StatusCode = "job_hold_customer", StatusLabel = "Customer Hold",
            Category = "hold", StartedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await _db.SaveChangesAsync();

        await Handler().Handle(new SalesOrderCancelledEvent(501), CancellationToken.None);

        (await ActiveJobHoldsAsync()).Count(h => h.EntityId == 1).Should().Be(1);
        (await ActiveJobHoldsAsync()).Should().Contain(h => h.EntityId == 2);
    }

    [Fact]
    public async Task Cancel_handler_publishes_the_cancelled_event_after_saving()
    {
        var repo = new Mock<ISalesOrderRepository>();
        var mediator = new Mock<IMediator>();
        var order = new SalesOrder { Id = 501, OrderNumber = "SO-00501", CustomerId = 1, Status = SalesOrderStatus.Confirmed };
        repo.Setup(r => r.FindAsync(501, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var saved = false;
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => saved = true).Returns(Task.CompletedTask);
        mediator.Setup(m => m.Publish(It.IsAny<SalesOrderCancelledEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => saved.Should().BeTrue())
            .Returns(Task.CompletedTask);

        await new CancelSalesOrderHandler(repo.Object, mediator.Object, Mock.Of<IClock>())
            .Handle(new CancelSalesOrderCommand(501), CancellationToken.None);

        mediator.Verify(m => m.Publish(It.Is<SalesOrderCancelledEvent>(e => e.SalesOrderId == 501),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
