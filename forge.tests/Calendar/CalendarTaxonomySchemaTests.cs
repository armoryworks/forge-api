using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Calendar;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-1, Stage 1a. Verifies the new calendar taxonomy tables
/// (calendar_super_groups / calendar_event_types) round-trip against the real
/// forge-db schema: EF mapping matches the applied DDL, the Super-Group → Event-Type
/// FK holds, and the unique key indexes reject duplicates. Runs on real Postgres
/// because unique/filtered indexes and FKs don't exist in the EF InMemory provider.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarTaxonomySchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SuperGroup_and_EventType_round_trip_with_fk()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        var group = new CalendarSuperGroup
        {
            Key = "compliance-osha",
            Name = "OSHA / Safety",
            DefaultVisible = false,
            RequiresTracking = true,
            SortOrder = 10,
            IsSystem = true,
        };
        var type = new CalendarEventType
        {
            SuperGroup = group,
            Key = "osha-300a",
            Name = "OSHA 300A posting",
            RequiresTracking = true,
            SortOrder = 1,
            IsSystem = true,
        };
        db.CalendarSuperGroups.Add(group);
        db.CalendarEventTypes.Add(type);
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var loaded = await verify.CalendarEventTypes
            .Include(t => t.SuperGroup)
            .SingleAsync(t => t.Key == "osha-300a");

        loaded.SuperGroup.Key.Should().Be("compliance-osha");
        loaded.SuperGroup.DefaultVisible.Should().BeFalse();
        loaded.RequiresTracking.Should().BeTrue();
    }

    [Fact]
    public async Task SuperGroup_key_is_unique()
    {
        await using var db = fixture.CreateContext();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        db.CalendarSuperGroups.Add(new CalendarSuperGroup { Key = "dup", Name = "First", SortOrder = 1 });
        await db.SaveChangesAsync();

        await using var db2 = fixture.CreateContext();
        db2.CalendarSuperGroups.Add(new CalendarSuperGroup { Key = "dup", Name = "Second", SortOrder = 2 });
        var act = async () => await db2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("ix_calendar_super_groups_key is unique");
    }
}
