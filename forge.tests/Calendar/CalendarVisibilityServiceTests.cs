using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities.Calendar;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-2, Stage 2. Verifies per-Super-Group read visibility: hidden
/// groups need an explicit role grant, Admin is unrestricted, and a null current user
/// (system context) is unrestricted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarVisibilityServiceTests(PostgresFixture fixture)
{
    private static async Task<int> SeedUserWithRoleAsync(AppDbContext db, string roleName)
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
            UserName = $"u-{tag}@test.com",
            Email = $"u-{tag}@test.com",
            FirstName = "Test",
            LastName = "User",
            Initials = "TU",
            AvatarColor = "#94a3b8",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Hidden_group_needs_a_role_grant()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarSuperGroupRoleVisibilities.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var vis = new CalendarSuperGroup { Key = "vis", Name = "Vis", DefaultVisible = true, SortOrder = 1 };
        var hidden = new CalendarSuperGroup { Key = "hidden", Name = "Hidden", DefaultVisible = false, SortOrder = 2 };
        db.CalendarSuperGroups.AddRange(vis, hidden);
        await db.SaveChangesAsync();

        db.CurrentUserId = await SeedUserWithRoleAsync(db, "Scheduler");

        var visible = await new CalendarVisibilityService(db).GetVisibleSuperGroupIdsAsync();
        visible.Should().NotBeNull();
        visible!.Should().Contain(vis.Id).And.NotContain(hidden.Id);

        db.CalendarSuperGroupRoleVisibilities.Add(new CalendarSuperGroupRoleVisibility { SuperGroupId = hidden.Id, Role = "Scheduler" });
        await db.SaveChangesAsync();

        var granted = await new CalendarVisibilityService(db).GetVisibleSuperGroupIdsAsync();
        granted!.Should().Contain(new[] { vis.Id, hidden.Id });
    }

    [Fact]
    public async Task Admin_is_unrestricted()
    {
        await using var db = fixture.CreateContext();
        db.CurrentUserId = await SeedUserWithRoleAsync(db, "Admin");

        var visible = await new CalendarVisibilityService(db).GetVisibleSuperGroupIdsAsync();
        visible.Should().BeNull("Admin sees every group");
    }

    [Fact]
    public async Task Null_current_user_is_unrestricted()
    {
        await using var db = fixture.CreateContext();
        db.CurrentUserId = null;

        var visible = await new CalendarVisibilityService(db).GetVisibleSuperGroupIdsAsync();
        visible.Should().BeNull("a system/background context is unrestricted");
    }
}
