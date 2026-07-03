using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Calendar;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-2/A-3, Stage 1c. Verifies the role-visibility allow-list
/// (unique per group+role, FK to Super-Group) and the saved-view entity (int[] layer
/// selections, scope) round-trip against the real forge-db schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarAclSavedViewSchemaTests(PostgresFixture fixture)
{
    private static async Task ResetAsync(Forge.Data.Context.AppDbContext db)
    {
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarSuperGroupRoleVisibilities.ExecuteDeleteAsync();
        await db.CalendarSavedViews.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RoleVisibility_and_SavedView_round_trip()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);

        var group = new CalendarSuperGroup { Key = "compliance-atf", Name = "ATF", DefaultVisible = false, SortOrder = 5, IsSystem = true };
        db.CalendarSuperGroups.Add(group);
        await db.SaveChangesAsync();

        db.CalendarSuperGroupRoleVisibilities.Add(new CalendarSuperGroupRoleVisibility { SuperGroupId = group.Id, Role = "ComplianceOfficer" });
        db.CalendarSavedViews.Add(new CalendarSavedView
        {
            Name = "My Compliance",
            RoleKey = "ComplianceOfficer",
            Scope = "module:compliance",
            SelectedSuperGroupIds = [group.Id],
            IsDefault = true,
        });
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var view = await verify.CalendarSavedViews.SingleAsync(v => v.Name == "My Compliance");
        view.SelectedSuperGroupIds.Should().ContainSingle().Which.Should().Be(group.Id);
        view.SelectedEventTypeIds.Should().BeEmpty();
        view.Scope.Should().Be("module:compliance");
        (await verify.CalendarSuperGroupRoleVisibilities.CountAsync(r => r.Role == "ComplianceOfficer")).Should().Be(1);
    }

    [Fact]
    public async Task RoleVisibility_grant_is_unique_per_group_role()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);

        var group = new CalendarSuperGroup { Key = "g", Name = "G", SortOrder = 1 };
        db.CalendarSuperGroups.Add(group);
        await db.SaveChangesAsync();
        db.CalendarSuperGroupRoleVisibilities.Add(new CalendarSuperGroupRoleVisibility { SuperGroupId = group.Id, Role = "Manager" });
        await db.SaveChangesAsync();

        await using var db2 = fixture.CreateContext();
        db2.CalendarSuperGroupRoleVisibilities.Add(new CalendarSuperGroupRoleVisibility { SuperGroupId = group.Id, Role = "Manager" });
        var act = async () => await db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("the (super_group_id, role) grant is unique");
    }
}
