using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.DomainEvents;

public class OperationalAlertNotifyTests
{
    private const int BuyerId = 1;
    private const int ManagerId = 2;
    private const int AdminId = 3;
    private const int ProjectManagerId = 4;
    private const int EngineerId = 5;
    private const int InactiveBuyerId = 6;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IHubContext<NotificationHub>> _hub = new();

    public OperationalAlertNotifyTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        _hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var roles = new[] { "Procurement", "Manager", "Admin", "PM", "Engineer" };
        for (var i = 0; i < roles.Length; i++)
            _db.Roles.Add(new IdentityRole<int> { Id = i + 1, Name = roles[i], NormalizedName = roles[i].ToUpperInvariant() });

        AddUser(BuyerId, 1);
        AddUser(ManagerId, 2);
        AddUser(AdminId, 3);
        AddUser(ProjectManagerId, 4);
        AddUser(EngineerId, 5);
        AddUser(InactiveBuyerId, 1, isActive: false);
        _db.SaveChanges();
    }

    private void AddUser(int id, int roleId, bool isActive = true)
    {
        _db.Users.Add(new ApplicationUser { Id = id, UserName = $"user{id}", Email = $"user{id}@example.test", IsActive = isActive });
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = id, RoleId = roleId });
    }

    private OnInventoryBelowReorder_Notify LowStockHandler() =>
        new(_db, _hub.Object, NullLogger<OnInventoryBelowReorder_Notify>.Instance);

    private OnJobCostOverrun_Notify OverrunHandler() =>
        new(_db, _hub.Object, NullLogger<OnJobCostOverrun_Notify>.Instance);

    [Fact]
    public async Task Low_stock_notifies_each_active_buyer_manager_and_admin_once()
    {
        _db.Parts.Add(new Part { Id = 50, PartNumber = "RS-100", Name = "Resin" });
        await _db.SaveChangesAsync();

        await LowStockHandler().Handle(new InventoryBelowReorderEvent(50, 0, 1100), CancellationToken.None);

        var notifications = _db.Notifications.ToList();
        notifications.Select(n => n.UserId).Should().BeEquivalentTo([BuyerId, ManagerId, AdminId]);
        notifications.Should().OnlyContain(n =>
            n.Type == OnInventoryBelowReorder_Notify.NotificationType
            && n.EntityType == "Part" && n.EntityId == 50
            && n.Title == "Low stock: RS-100"
            && n.Message.Contains("reorder point of 1,100"));
    }

    [Fact]
    public async Task A_second_low_stock_run_adds_nothing_while_the_alert_is_open()
    {
        _db.Parts.Add(new Part { Id = 50, PartNumber = "RS-100", Name = "Resin" });
        await _db.SaveChangesAsync();
        var handler = LowStockHandler();

        await handler.Handle(new InventoryBelowReorderEvent(50, 0, 1100), CancellationToken.None);
        await handler.Handle(new InventoryBelowReorderEvent(50, 0, 1100), CancellationToken.None);

        _db.Notifications.Count().Should().Be(3);
    }

    [Fact]
    public async Task A_dismissed_low_stock_alert_is_raised_again_on_the_next_run()
    {
        _db.Parts.Add(new Part { Id = 50, PartNumber = "RS-100", Name = "Resin" });
        await _db.SaveChangesAsync();
        var handler = LowStockHandler();
        await handler.Handle(new InventoryBelowReorderEvent(50, 0, 1100), CancellationToken.None);
        _db.Notifications.Single(n => n.UserId == BuyerId).IsDismissed = true;
        await _db.SaveChangesAsync();

        await handler.Handle(new InventoryBelowReorderEvent(50, 0, 1100), CancellationToken.None);

        _db.Notifications.Count(n => n.UserId == BuyerId).Should().Be(2);
        _db.Notifications.Count().Should().Be(4);
    }

    [Fact]
    public async Task Low_stock_for_a_missing_part_does_nothing()
    {
        await LowStockHandler().Handle(new InventoryBelowReorderEvent(999, 0, 10), CancellationToken.None);

        _db.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Cost_overrun_notifies_managers_project_managers_and_the_assignee_once()
    {
        _db.Jobs.Add(new Job { Id = 6, JobNumber = "J-6", Title = "Housing run", TrackTypeId = 1, CurrentStageId = 1, AssigneeId = EngineerId });
        await _db.SaveChangesAsync();
        var handler = OverrunHandler();

        await handler.Handle(new JobCostOverrunEvent(6, 100m, 195m, 0.95m), CancellationToken.None);
        await handler.Handle(new JobCostOverrunEvent(6, 100m, 195m, 0.95m), CancellationToken.None);

        var notifications = _db.Notifications.ToList();
        notifications.Select(n => n.UserId).Should().BeEquivalentTo([ManagerId, ProjectManagerId, EngineerId]);
        notifications.Should().OnlyContain(n =>
            n.Type == OnJobCostOverrun_Notify.NotificationType
            && n.EntityType == "Job" && n.EntityId == 6
            && n.Title == "Cost overrun: J-6"
            && n.Message.Contains("195.00")
            && n.Message.Contains("95% overrun"));
    }
}
