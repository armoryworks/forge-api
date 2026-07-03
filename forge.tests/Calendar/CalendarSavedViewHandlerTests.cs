using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Calendar;
using Forge.Core.Entities.Calendar;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-3, Stage 4. A user's saved-view read returns their personal views
/// plus role-default views for their roles; create makes a personal view they own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarSavedViewHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Create_then_get_returns_personal_and_role_default_views()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarSavedViews.ExecuteDeleteAsync();

        var role = new IdentityRole<int> { Name = "SViewer", NormalizedName = "SVIEWER" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        var tag = Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { UserName = $"u{tag}@t.com", Email = $"u{tag}@t.com", FirstName = "T", LastName = "U", Initials = "TU", AvatarColor = "#888" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();

        db.CalendarSavedViews.Add(new CalendarSavedView
        {
            Name = "Role Default",
            OwnerUserId = null,
            RoleKey = "SViewer",
            Scope = "master",
            SelectedSuperGroupIds = [1],
            IsDefault = true,
        });
        await db.SaveChangesAsync();

        db.CurrentUserId = user.Id;
        var created = await new CreateCalendarSavedViewHandler(db)
            .Handle(new CreateCalendarSavedViewCommand("My View", "master", [2, 3], []), default);
        created.OwnerUserId.Should().Be(user.Id);
        created.SelectedSuperGroupIds.Should().BeEquivalentTo([2, 3]);

        var views = await new GetCalendarSavedViewsHandler(db)
            .Handle(new GetCalendarSavedViewsQuery(null), default);
        views.Select(v => v.Name).Should().Contain(["My View", "Role Default"]);
    }
}
