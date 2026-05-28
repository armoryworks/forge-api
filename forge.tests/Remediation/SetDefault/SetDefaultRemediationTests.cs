using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.CompanyLocations;
using Forge.Api.Features.TimeTracking;
using Forge.Api.Features.WorkingCalendars;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Remediation.SetDefault;

/// <summary>
/// BE-1 / F-12-BE-01 (working-calendar), F-12-BE-02 (company-location), F-14-BE-02
/// (overtime-rule): each "set default" path cleared the prior default and set the new
/// one in a single batched SaveChanges. Against a Postgres filtered unique index
/// (<c>is_default = true</c>), EF's non-deterministic statement ordering can put the
/// set-new write before the clear-old one, momentarily yielding two default rows and a
/// 500. The fix performs the clear via a discrete <c>ExecuteUpdate</c> statement inside
/// a transaction before setting the target. These run against a real Postgres because the
/// constraint (and <c>ExecuteUpdate</c>) do not exist in the EF InMemory provider.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SetDefaultRemediationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SetDefaultWorkingCalendar_swaps_atomically_without_unique_violation()
    {
        await using var db = fixture.CreateContext();
        await db.WorkingCalendars.ExecuteDeleteAsync();

        var current = new WorkingCalendar { Name = "Default Cal", TimeZone = "UTC", IsDefault = true, IsActive = true };
        var target = new WorkingCalendar { Name = "Night Shift Cal", TimeZone = "UTC", IsDefault = false, IsActive = true };
        db.WorkingCalendars.AddRange(current, target);
        await db.SaveChangesAsync();

        var handler = new SetDefaultWorkingCalendarHandler(db);

        var act = async () => await handler.Handle(new SetDefaultWorkingCalendarCommand(target.Id), default);
        await act.Should().NotThrowAsync("the default swap must clear the prior default before setting the new one");

        await using var verify = fixture.CreateContext();
        var defaults = await verify.WorkingCalendars.Where(c => c.IsDefault).ToListAsync();
        defaults.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task SetDefaultCompanyLocation_swaps_atomically_without_unique_violation()
    {
        await using var db = fixture.CreateContext();
        await db.CompanyLocations.ExecuteDeleteAsync();

        var current = new CompanyLocation { Name = "HQ", Line1 = "1 Main", City = "Denver", State = "CO", PostalCode = "80202", IsDefault = true, IsActive = true };
        var target = new CompanyLocation { Name = "Plant 2", Line1 = "9 Industrial", City = "Aurora", State = "CO", PostalCode = "80011", IsDefault = false, IsActive = true };
        db.CompanyLocations.AddRange(current, target);
        await db.SaveChangesAsync();

        var handler = new SetDefaultCompanyLocationHandler(db);

        var act = async () => await handler.Handle(new SetDefaultCompanyLocationCommand(target.Id), default);
        await act.Should().NotThrowAsync("the default swap must clear the prior default before setting the new one");

        await using var verify = fixture.CreateContext();
        var defaults = await verify.CompanyLocations.Where(x => x.IsDefault).ToListAsync();
        defaults.Should().ContainSingle().Which.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task CreateOvertimeRule_as_default_swaps_atomically_without_unique_violation()
    {
        await using var db = fixture.CreateContext();
        await db.Set<OvertimeRule>().ExecuteDeleteAsync();

        db.Set<OvertimeRule>().Add(new OvertimeRule { Name = "Legacy Default", IsDefault = true });
        await db.SaveChangesAsync();

        var handler = new CreateOvertimeRuleHandler(db);
        var request = new CreateOvertimeRuleRequestModel(
            Name: "CA Daily OT",
            DailyThresholdHours: 8m,
            WeeklyThresholdHours: 40m,
            OvertimeMultiplier: 1.5m,
            DoubletimeThresholdDailyHours: 12m,
            DoubletimeThresholdWeeklyHours: null,
            DoubletimeMultiplier: 2.0m,
            IsDefault: true,
            ApplyDailyBeforeWeekly: true);

        OvertimeRuleResponseModel? created = null;
        var act = async () => created = await handler.Handle(new CreateOvertimeRuleCommand(request), default);
        await act.Should().NotThrowAsync("creating a new default must clear the prior default before inserting");

        await using var verify = fixture.CreateContext();
        var defaults = await verify.Set<OvertimeRule>().Where(r => r.IsDefault).ToListAsync();
        defaults.Should().ContainSingle().Which.Id.Should().Be(created!.Id);
    }
}
