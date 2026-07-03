using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Calendar;
using Forge.Api.Services;
using Forge.Core.Entities.Calendar;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-3, Stage 3 (read API). The layer-list endpoint returns only the
/// Super-Groups the current user may see, each with its Event-Types.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetCalendarSuperGroupsHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Returns_only_visible_groups_with_their_types()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarSuperGroupRoleVisibilities.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var vis = new CalendarSuperGroup { Key = "ops", Name = "Ops", DefaultVisible = true, SortOrder = 1 };
        var hidden = new CalendarSuperGroup { Key = "atf", Name = "ATF", DefaultVisible = false, SortOrder = 2 };
        db.CalendarSuperGroups.AddRange(vis, hidden);
        await db.SaveChangesAsync();
        db.CalendarEventTypes.Add(new CalendarEventType { SuperGroupId = vis.Id, Key = "milestone", Name = "Milestone", SortOrder = 1 });
        db.CalendarEventTypes.Add(new CalendarEventType { SuperGroupId = hidden.Id, Key = "afmer", Name = "AFMER", SortOrder = 1 });
        await db.SaveChangesAsync();

        // A user whose role has no grant to the hidden group.
        var role = new IdentityRole<int> { Name = "Sched", NormalizedName = "SCHED" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"u{tag}@t.com", Email = $"u{tag}@t.com", FirstName = "T", LastName = "U", Initials = "TU", AvatarColor = "#888" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        db.CurrentUserId = user.Id;

        var handler = new GetCalendarSuperGroupsHandler(db, new CalendarVisibilityService(db));
        var result = await handler.Handle(new GetCalendarSuperGroupsQuery(), default);

        result.Should().ContainSingle();
        result[0].Key.Should().Be("ops");
        result[0].EventTypes.Should().ContainSingle().Which.Key.Should().Be("milestone");
    }
}
