using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class SalesOrderEventMessageTests
{
    private const string OrderNumber = "SO-00605";
    private const int ManagerUserId = 42;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IClock> _clock = new();
    private readonly Mock<IHubContext<NotificationHub>> _hub = new();

    public SalesOrderEventMessageTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        _hub.SetupGet(h => h.Clients).Returns(clients.Object);
    }

    private async Task<SalesOrder> SeedOrderAsync(string roleName = "Manager")
    {
        _db.Roles.Add(new IdentityRole<int> { Id = 9, Name = roleName, NormalizedName = roleName.ToUpperInvariant() });
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = ManagerUserId, RoleId = 9 });
        var customer = new Customer { Id = 1, Name = "Event Co" };
        _db.Customers.Add(customer);
        var so = new SalesOrder
        {
            Id = 605, OrderNumber = OrderNumber, CustomerId = customer.Id, Customer = customer,
            Status = SalesOrderStatus.Confirmed,
        };
        so.Lines.Add(new SalesOrderLine { Id = 606, Description = "Widget", Quantity = 2m, UnitPrice = 1m, LineNumber = 1 });
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        return so;
    }

    private static void ShouldNameTheOrderOnce(string? text)
    {
        text.Should().Contain(OrderNumber);
        text.Should().NotContain("SO-SO-");
    }

    [Fact]
    public async Task Confirmed_follow_up_title_names_the_order_once()
    {
        var so = await SeedOrderAsync();
        var handler = new OnSalesOrderConfirmed_CreateFollowUps(
            _db, _clock.Object, NullLogger<OnSalesOrderConfirmed_CreateFollowUps>.Instance);

        await handler.Handle(new SalesOrderConfirmedEvent(so.Id, ManagerUserId), CancellationToken.None);

        var task = _db.FollowUpTasks.Single();
        task.Title.Should().Be($"Create jobs for {OrderNumber}");
        ShouldNameTheOrderOnce(task.Title);
    }

    [Fact]
    public async Task Delivery_at_risk_title_names_the_order_once()
    {
        await SeedOrderAsync();
        _db.Jobs.Add(new Job
        {
            Id = 1, JobNumber = "J-1", Title = "Build", TrackTypeId = 1, CurrentStageId = 1,
            SalesOrderLineId = 606, DueDate = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await _db.SaveChangesAsync();
        var handler = new OnDeliveryDateChanged_CascadeCheck(
            _db, _clock.Object, NullLogger<OnDeliveryDateChanged_CascadeCheck>.Instance);

        await handler.Handle(new DeliveryDateChangedEvent(
            606,
            new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero),
            ManagerUserId), CancellationToken.None);

        var task = _db.FollowUpTasks.Single();
        task.Title.Should().Be($"Delivery at risk for {OrderNumber} line 1");
        ShouldNameTheOrderOnce(task.Title);
    }

    [Fact]
    public async Task Ship_ready_follow_up_and_notification_name_the_order_once()
    {
        await SeedOrderAsync();
        _db.JobStages.Add(new JobStage { Id = 5, TrackTypeId = 1, Name = "Shipped", Code = "shipped" });
        _db.Jobs.Add(new Job
        {
            Id = 1, JobNumber = "J-1", Title = "Build", TrackTypeId = 1, CurrentStageId = 5, SalesOrderLineId = 606,
        });
        await _db.SaveChangesAsync();
        var handler = new OnJobStageChanged_CheckShipReady(
            _db, _clock.Object, _hub.Object, NullLogger<OnJobStageChanged_CheckShipReady>.Instance);

        await handler.Handle(new JobStageChangedEvent(1, 4, 5, ManagerUserId), CancellationToken.None);

        var task = _db.FollowUpTasks.Single();
        task.Title.Should().Be($"SO line ready to ship — {OrderNumber}");
        ShouldNameTheOrderOnce(task.Title);
        var notification = _db.Notifications.Single();
        notification.Message.Should().StartWith($"All jobs for {OrderNumber} line 1");
        ShouldNameTheOrderOnce(notification.Message);
    }

    [Fact]
    public async Task Shipment_delivered_follow_up_and_notification_name_the_order_once()
    {
        var so = await SeedOrderAsync("PM");
        _db.Shipments.Add(new Shipment { Id = 3, ShipmentNumber = "SHP-3", SalesOrderId = so.Id });
        await _db.SaveChangesAsync();
        var handler = new OnShipmentDelivered_Notify(
            _db, _clock.Object, _hub.Object, NullLogger<OnShipmentDelivered_Notify>.Instance);

        await handler.Handle(new ShipmentDeliveredEvent(3, so.Id, ManagerUserId), CancellationToken.None);

        var task = _db.FollowUpTasks.Single();
        task.Title.Should().Be($"Shipment delivered — {OrderNumber}");
        var notification = _db.Notifications.Single();
        notification.Message.Should().Be($"Shipment SHP-3 for {OrderNumber} (Event Co) has been delivered.");
        ShouldNameTheOrderOnce(notification.Message);
    }

    [Fact]
    public async Task Material_shortfall_notification_names_the_order_once()
    {
        var so = await SeedOrderAsync();
        var assembly = new Part { Id = 20, PartNumber = "ASM-20", Name = "Assembly" };
        var material = new Part { Id = 21, PartNumber = "MAT-21", Name = "Material" };
        _db.Parts.AddRange(assembly, material);
        _db.BOMLines.Add(new BOMLine
        {
            ParentPartId = assembly.Id, ChildPartId = material.Id, Quantity = 3m, SourceType = BOMSourceType.Buy,
        });
        _db.SalesOrderLines.Single(l => l.Id == 606).PartId = assembly.Id;
        await _db.SaveChangesAsync();
        var handler = new OnSalesOrderConfirmed_CheckBomMaterials(
            _db, _clock.Object, NullLogger<OnSalesOrderConfirmed_CheckBomMaterials>.Instance);

        await handler.Handle(new SalesOrderConfirmedEvent(so.Id, ManagerUserId), CancellationToken.None);

        var notification = _db.Notifications.Single();
        notification.Message.Should().EndWith($"{OrderNumber}.");
        ShouldNameTheOrderOnce(notification.Message);
    }
}
