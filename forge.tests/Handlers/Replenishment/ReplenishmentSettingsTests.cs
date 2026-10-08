using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.FollowUpTasks;
using Forge.Api.Features.Replenishment;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Replenishment;

public class ReplenishmentSettingsTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<ApplicationUser> SeedUserAsync(string lastName, string? role, bool active = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"{lastName}@test.local",
            Email = $"{lastName}@test.local",
            FirstName = "Sam",
            LastName = lastName,
            Initials = "SX",
            AvatarColor = "#888",
            IsActive = active,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (role is not null)
        {
            var roleRow = await _db.Roles.FirstOrDefaultAsync(r => r.Name == role);
            if (roleRow is null)
            {
                roleRow = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
                _db.Roles.Add(roleRow);
                await _db.SaveChangesAsync();
            }
            _db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = roleRow.Id });
            await _db.SaveChangesAsync();
        }

        return user;
    }

    private UpdateReplenishmentSettingsHandler UpdateHandler() =>
        new(_db, new SystemSettingRepository(_db));

    private GetReplenishmentSettingsHandler GetHandler() =>
        new(new SystemSettingRepository(_db));

    [Fact]
    public async Task Assignee_round_trips_and_logs_activity_on_the_setting()
    {
        var manager = await SeedUserAsync("Lead", "Manager");

        var saved = await UpdateHandler().Handle(new UpdateReplenishmentSettingsCommand(manager.Id), default);
        var read = await GetHandler().Handle(new GetReplenishmentSettingsQuery(), default);

        saved.AssigneeUserId.Should().Be(manager.Id);
        read.AssigneeUserId.Should().Be(manager.Id);
        var setting = await _db.SystemSettings.SingleAsync(s => s.Key == ReplenishmentAssignee.SettingKey);
        setting.Value.Should().Be(manager.Id.ToString());
        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "SystemSetting" && a.Action == "updated");
        log.EntityId.Should().Be(setting.Id);
        log.Description.Should().Contain("Lead, Sam");
    }

    [Fact]
    public async Task Clearing_the_assignee_stores_no_user()
    {
        var admin = await SeedUserAsync("Boss", "Admin");
        await UpdateHandler().Handle(new UpdateReplenishmentSettingsCommand(admin.Id), default);

        await UpdateHandler().Handle(new UpdateReplenishmentSettingsCommand(null), default);

        (await GetHandler().Handle(new GetReplenishmentSettingsQuery(), default)).AssigneeUserId.Should().BeNull();
        (await _db.ActivityLogs.CountAsync(a => a.EntityType == "SystemSetting" && a.Action == "updated")).Should().Be(2);
    }

    [Fact]
    public async Task Non_manager_is_rejected()
    {
        var engineer = await SeedUserAsync("Floor", "Engineer");

        var act = () => UpdateHandler().Handle(new UpdateReplenishmentSettingsCommand(engineer.Id), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Single().ErrorMessage
            .Should().Be("Pick an admin or manager; only they can approve suggestions.");
        (await _db.SystemSettings.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Inactive_manager_is_rejected()
    {
        var former = await SeedUserAsync("Former", "Manager", active: false);

        var act = () => UpdateHandler().Handle(new UpdateReplenishmentSettingsCommand(former.Id), default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Follow_up_tasks_carry_a_readable_source_label()
    {
        var owner = await SeedUserAsync("Owner", "Manager");
        var part = new Part { PartNumber = "PN-77", Name = "Bracket" };
        _db.Parts.Add(part);
        var job = new Job { JobNumber = "J-58", Title = "Run", TrackTypeId = 1, CurrentStageId = 1 };
        _db.Jobs.Add(job);
        var po = new PurchaseOrder { PONumber = "PO-00012", VendorId = 1 };
        _db.PurchaseOrders.Add(po);
        var invoice = new Invoice { InvoiceNumber = "INV-9", CustomerId = 1 };
        _db.Invoices.Add(invoice);
        var rma = new CustomerReturn { ReturnNumber = "RMA-3", CustomerId = 1 };
        _db.CustomerReturns.Add(rma);
        await _db.SaveChangesAsync();
        var suggestion = new ReorderSuggestion { PartId = part.Id, SuggestedQuantity = 1m };
        _db.ReorderSuggestions.Add(suggestion);
        await _db.SaveChangesAsync();

        void AddTask(string type, int id) => _db.FollowUpTasks.Add(new FollowUpTask
        {
            Title = type,
            AssignedToUserId = owner.Id,
            SourceEntityType = type,
            SourceEntityId = id,
        });
        AddTask("Job", job.Id);
        AddTask("PurchaseOrder", po.Id);
        AddTask("Invoice", invoice.Id);
        AddTask("Part", part.Id);
        AddTask("CustomerReturn", rma.Id);
        AddTask("ReorderSuggestion", suggestion.Id);
        AddTask("SalesOrderLine", 999);
        await _db.SaveChangesAsync();

        var tasks = await new GetFollowUpTasksHandler(_db)
            .Handle(new GetFollowUpTasksQuery(owner.Id, FollowUpStatus.Open), default);

        tasks.ToDictionary(t => t.SourceEntityType!, t => t.SourceEntityLabel).Should().BeEquivalentTo(
            new Dictionary<string, string?>
            {
                ["Job"] = "J-58",
                ["PurchaseOrder"] = "PO-00012",
                ["Invoice"] = "INV-9",
                ["Part"] = "PN-77",
                ["CustomerReturn"] = "RMA-3",
                ["ReorderSuggestion"] = "PN-77",
                ["SalesOrderLine"] = null,
            });
    }
}
