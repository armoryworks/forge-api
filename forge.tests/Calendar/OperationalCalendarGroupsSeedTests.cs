using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Calendar;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// System-generated PO delivery, SO delivery and job due-date events sit in default-hidden
/// operational groups, so a reader without the source module's role does not see vendor names
/// or order lines. Existing events move off the default-visible "other" type; nothing else does.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalCalendarGroupsSeedTests(PostgresFixture fixture)
{
    private static Event SystemEvent(string title, bool systemGenerated = true, int? typeId = null) => new()
    {
        Title = title,
        StartTime = DateTimeOffset.UtcNow,
        EndTime = DateTimeOffset.UtcNow,
        EventType = EventType.Other,
        EventTypeId = typeId,
        IsAllDay = true,
        IsSystemGenerated = systemGenerated,
        CreatedByUserId = 1,
    };

    private static async Task<int> UserWithRoleAsync(AppDbContext db, string roleName)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role is null)
        {
            role = new IdentityRole<int> { Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser
        {
            UserName = $"u-{tag}@test.com", Email = $"u-{tag}@test.com",
            FirstName = "Test", LastName = "User", Initials = "TU", AvatarColor = "#94a3b8",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Seed_moves_generated_order_events_into_hidden_groups_and_grants_their_roles()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarSuperGroupRoleVisibilities.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var po = SystemEvent("PO-PO-00012 Expected Delivery from Steel Supply");
        var so = SystemEvent("SO-00031 Line 1 Delivery: Bracket");
        var job = SystemEvent("Job Due: J-1007 - Bracket run");
        var manual = SystemEvent("PO-PO-00099 Expected Delivery from Someone", systemGenerated: false);
        db.Events.AddRange(po, so, job, manual);
        await db.SaveChangesAsync();

        await SeedData.SeedCalendarTaxonomyAsync(db);
        var regulatory = new CalendarSuperGroup { Key = "reg-test", Name = "Reg", SortOrder = 200 };
        db.CalendarSuperGroups.Add(regulatory);
        await db.SaveChangesAsync();
        var regType = new CalendarEventType { SuperGroupId = regulatory.Id, Key = "reg-test-type", Name = "Reg", SortOrder = 1 };
        db.CalendarEventTypes.Add(regType);
        await db.SaveChangesAsync();
        var watchtower = SystemEvent("Job Due: review regulatory proposal", typeId: regType.Id);
        db.Events.Add(watchtower);
        await db.SaveChangesAsync();

        await SeedData.SeedOperationalCalendarGroupsAsync(db);
        await SeedData.SeedOperationalCalendarGroupsAsync(db);

        await using var verify = fixture.CreateContext();
        var types = await verify.CalendarEventTypes.ToDictionaryAsync(t => t.Key, t => t.Id);
        var events = await verify.Events.AsNoTracking().ToDictionaryAsync(e => e.Id);
        events[po.Id].EventTypeId.Should().Be(types[SeedData.PoDeliveryEventTypeKey]);
        events[so.Id].EventTypeId.Should().Be(types[SeedData.SoDeliveryEventTypeKey]);
        events[job.Id].EventTypeId.Should().Be(types[SeedData.JobDueEventTypeKey]);
        events[manual.Id].EventTypeId.Should().Be(types["other"], "a hand-made event is left alone");
        events[watchtower.Id].EventTypeId.Should().Be(regType.Id, "an event already typed elsewhere is never remapped");

        var groups = await verify.CalendarSuperGroups.Where(g => new[] { "purchasing", "sales", "production" }.Contains(g.Key)).ToListAsync();
        groups.Should().HaveCount(3).And.OnlyContain(g => !g.DefaultVisible);
        var purchasingId = groups.Single(g => g.Key == "purchasing").Id;
        (await verify.CalendarSuperGroupRoleVisibilities.Where(v => v.SuperGroupId == purchasingId).Select(v => v.Role).ToListAsync())
            .Should().BeEquivalentTo("Manager", "OfficeManager", "Procurement");

        verify.CurrentUserId = await UserWithRoleAsync(verify, "ProductionWorker");
        var visible = await new CalendarVisibilityService(verify).GetVisibleSuperGroupIdsAsync();
        visible.Should().NotContain(purchasingId).And.NotContain(groups.Single(g => g.Key == "sales").Id);
        visible.Should().Contain(groups.Single(g => g.Key == "production").Id);
    }
}
