using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Replenishment;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class ReorderAnalysisMakeTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShopCalendar EveryDay = new(0x7F, new HashSet<DateOnly>());

    private sealed class FixedClock : Forge.Core.Interfaces.IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private ReorderAnalysisJob NewJob() => new(
        _db, new FixedClock(), new PartSourcingResolver(_db),
        NullLogger<ReorderAnalysisJob>.Instance,
        new StubCapabilitySnapshotProvider("CAP-PLAN-SAFETYSTOCK"));

    private async Task<Part> SeedMakePartAsync(
        string partNumber,
        decimal? reorderPoint = 50m,
        decimal stock = 10m,
        decimal burnPerDay = 5m,
        decimal runMinutesEach = 48m,
        ProcurementSource source = ProcurementSource.Make)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = source,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            ReorderPoint = reorderPoint,
            SafetyStockDays = 2,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();

        if (runMinutesEach > 0)
            _db.Operations.Add(new Operation
            {
                PartId = part.Id,
                StepNumber = 10,
                Title = "Mold",
                RunMinutesEach = runMinutesEach,
            });

        if (stock > 0)
            _db.BinContents.Add(new BinContent
            {
                EntityType = "part",
                EntityId = part.Id,
                Quantity = stock,
                LocationId = 1,
            });

        for (var d = 1; d <= 90; d++)
            _db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = part.Id,
                Quantity = burnPerDay,
                Reason = BinMovementReason.Ship,
                MovedAt = FixedNow.AddDays(-d),
            });

        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Job> SeedOpenJobAsync(
        int partId, decimal quantity, DateTimeOffset? dueDate = null,
        bool archived = false, bool completed = false, JobDisposition? disposition = null)
    {
        var job = new Job
        {
            JobNumber = $"J-{Guid.NewGuid():N}"[..10],
            Title = "Build",
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = partId,
            DueDate = dueDate,
            IsArchived = archived,
            CompletedDate = completed ? FixedNow.AddDays(-1) : null,
            Disposition = disposition,
        };
        job.JobParts.Add(new JobPart { PartId = partId, Quantity = quantity });
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<ApplicationUser> SeedUserAsync(string lastName, string role, bool active = true)
    {
        var roleRow = await _db.Roles.FirstOrDefaultAsync(r => r.Name == role);
        if (roleRow is null)
        {
            roleRow = new IdentityRole<int> { Name = role, NormalizedName = role.ToUpperInvariant() };
            _db.Roles.Add(roleRow);
        }

        var user = new ApplicationUser
        {
            UserName = $"{lastName}@test.local",
            Email = $"{lastName}@test.local",
            FirstName = "Pat",
            LastName = lastName,
            Initials = "PX",
            AvatarColor = "#888",
            IsActive = active,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = roleRow.Id });
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task SetAssigneeAsync(int userId)
    {
        _db.SystemSettings.Add(new SystemSetting { Key = ReplenishmentAssignee.SettingKey, Value = userId.ToString() });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Make_part_below_its_reorder_point_gets_a_make_suggestion_from_open_jobs_and_routing()
    {
        var part = await SeedMakePartAsync("MAKE-100");
        var due = FixedNow.AddDays(3);
        await SeedOpenJobAsync(part.Id, 20m, due);
        await SeedOpenJobAsync(part.Id, 500m, archived: true);
        await SeedOpenJobAsync(part.Id, 500m, completed: true);
        await SeedOpenJobAsync(part.Id, 500m, disposition: JobDisposition.Scrap);

        await NewJob().RunAnalysisAsync();

        var suggestion = await _db.ReorderSuggestions.SingleAsync(s => s.PartId == part.Id);
        suggestion.VendorId.Should().BeNull();
        suggestion.IncomingPoQuantity.Should().Be(20m);
        suggestion.EarliestPoArrival.Should().Be(due);
        suggestion.SuggestedQuantity.Should().Be(15m);
    }

    [Fact]
    public async Task Make_part_without_routing_uses_a_seven_day_lead_time()
    {
        var part = await SeedMakePartAsync("MAKE-NOROUTE", runMinutesEach: 0m);

        await NewJob().RunAnalysisAsync();

        var suggestion = await _db.ReorderSuggestions.SingleAsync(s => s.PartId == part.Id);
        suggestion.SuggestedQuantity.Should().Be(45m);
    }

    [Fact]
    public async Task Open_job_that_covers_demand_yields_no_suggestion()
    {
        var part = await SeedMakePartAsync("MAKE-COVERED");
        await SeedOpenJobAsync(part.Id, 60m, FixedNow.AddDays(2));

        await NewJob().RunAnalysisAsync();

        (await _db.ReorderSuggestions.AnyAsync(s => s.PartId == part.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Phantom_parts_are_skipped()
    {
        var part = await SeedMakePartAsync("PHANTOM-1", source: ProcurementSource.Phantom);

        await NewJob().RunAnalysisAsync();

        (await _db.ReorderSuggestions.AnyAsync(s => s.PartId == part.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Assignee_gets_one_task_per_new_suggestion_and_the_only_notification()
    {
        var assignee = await SeedUserAsync("Planner", "Manager");
        var otherManager = await SeedUserAsync("Other", "Admin");
        await SetAssigneeAsync(assignee.Id);
        var part = await SeedMakePartAsync("MAKE-TASK");

        await NewJob().RunAnalysisAsync();

        var suggestion = await _db.ReorderSuggestions.SingleAsync(s => s.PartId == part.Id);
        var task = await _db.FollowUpTasks.SingleAsync();
        task.Title.Should().Be("Make 15 x MAKE-TASK");
        task.AssignedToUserId.Should().Be(assignee.Id);
        task.SourceEntityType.Should().Be("ReorderSuggestion");
        task.SourceEntityId.Should().Be(suggestion.Id);
        task.TriggerType.Should().Be(FollowUpTriggerType.ReorderSuggested);
        task.Status.Should().Be(FollowUpStatus.Open);
        task.Description.Should().Contain("Available 10").And.Contain("reorder point 50")
            .And.Contain("daily use 5").And.Contain("lead time 4 day(s)");
        task.DueDate.Should().Be(new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero));

        var notified = await _db.Notifications.Select(n => n.UserId).ToListAsync();
        notified.Should().Equal(assignee.Id);
        notified.Should().NotContain(otherManager.Id);

        await NewJob().RunAnalysisAsync();
        (await _db.FollowUpTasks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Task_due_date_is_stockout_minus_lead_time()
    {
        var assignee = await SeedUserAsync("Planner", "Manager");
        await SetAssigneeAsync(assignee.Id);
        var part = await SeedMakePartAsync("MAKE-LATER", reorderPoint: 200m, stock: 100m);

        await NewJob().RunAnalysisAsync();

        var suggestion = await _db.ReorderSuggestions.SingleAsync(s => s.PartId == part.Id);
        var task = await _db.FollowUpTasks.SingleAsync();
        suggestion.ProjectedStockoutDate.Should().Be(FixedNow.AddDays(20));
        task.DueDate.Should().Be(FixedNow.AddDays(16));
    }

    [Fact]
    public async Task Task_due_date_is_never_before_today()
    {
        var assignee = await SeedUserAsync("Planner", "Manager");
        await SetAssigneeAsync(assignee.Id);
        await SeedMakePartAsync("MAKE-EMPTY", stock: 0m);

        await NewJob().RunAnalysisAsync();

        var task = await _db.FollowUpTasks.SingleAsync();
        task.DueDate.Should().Be(new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Without_an_assignee_no_task_is_made_and_admins_and_managers_are_notified()
    {
        var admin = await SeedUserAsync("Admin", "Admin");
        var manager = await SeedUserAsync("Manager", "Manager");
        await SeedMakePartAsync("MAKE-NOASSIGNEE");

        await NewJob().RunAnalysisAsync();

        (await _db.FollowUpTasks.AnyAsync()).Should().BeFalse();
        (await _db.Notifications.Select(n => n.UserId).ToListAsync())
            .Should().BeEquivalentTo(new[] { admin.Id, manager.Id });
    }

    [Fact]
    public async Task Inactive_assignee_is_ignored()
    {
        var gone = await SeedUserAsync("Gone", "Manager", active: false);
        await SetAssigneeAsync(gone.Id);
        await SeedMakePartAsync("MAKE-INACTIVE");

        await NewJob().RunAnalysisAsync();

        (await _db.FollowUpTasks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_suggestion_dismisses_its_open_task()
    {
        var assignee = await SeedUserAsync("Planner", "Manager");
        await SetAssigneeAsync(assignee.Id);
        var part = await SeedMakePartAsync("MAKE-EXPIRE");

        await NewJob().RunAnalysisAsync();
        var suggestion = await _db.ReorderSuggestions.SingleAsync(s => s.PartId == part.Id);

        await SeedOpenJobAsync(part.Id, 100m, FixedNow.AddDays(2));
        await NewJob().RunAnalysisAsync();

        suggestion.Status.Should().Be(ReorderSuggestionStatus.Expired);
        var task = await _db.FollowUpTasks.SingleAsync();
        task.Status.Should().Be(FollowUpStatus.Dismissed);
        task.DismissedAt.Should().Be(FixedNow);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(15, 2)]
    [InlineData(100, 10)]
    public void MakeLeadTimeDays_rounds_planned_minutes_up_to_whole_shifts(int quantity, int expected)
    {
        var op = new Operation { RunMinutesEach = 48m };

        OperationTimeMath.MakeLeadTimeDays([op], quantity, EveryDay, DateOnly.FromDateTime(FixedNow.UtcDateTime))
            .Should().Be(expected);
    }

    [Fact]
    public void MakeLeadTimeDays_is_seven_without_a_routing()
    {
        OperationTimeMath.MakeLeadTimeDays([], 50m, EveryDay, DateOnly.FromDateTime(FixedNow.UtcDateTime))
            .Should().Be(7);
    }
}
