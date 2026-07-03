using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Tests.Helpers;

namespace Forge.Tests.Calendar;

/// <summary>
/// compliance-calendar A-5, Stage 6. The compliance-bucket seeder creates default-hidden,
/// tracking-tier Super-Groups (ATF/FDA industry-gated) + starter event types, idempotently.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CalendarComplianceSeedTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeds_industry_gated_compliance_buckets()
    {
        await using var db = fixture.CreateContext();
        await db.Events.ExecuteDeleteAsync();
        await db.CalendarEventTypes.ExecuteDeleteAsync();
        await db.CalendarSuperGroups.ExecuteDeleteAsync();

        await SeedData.SeedComplianceBucketsAsync(db);
        await SeedData.SeedComplianceBucketsAsync(db); // idempotent

        await using var verify = fixture.CreateContext();
        var atf = await verify.CalendarSuperGroups.SingleAsync(g => g.Key == "atf-firearms");
        atf.IndustryGate.Should().Be("firearms");
        atf.DefaultVisible.Should().BeFalse();
        atf.RequiresTracking.Should().BeTrue();

        (await verify.CalendarEventTypes.AnyAsync(t => t.Key == "atf-afmer")).Should().BeTrue();
        (await verify.CalendarSuperGroups.CountAsync()).Should().Be(8);
    }
}
