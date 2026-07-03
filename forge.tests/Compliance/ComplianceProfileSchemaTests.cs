using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Data;
using Forge.Core.Entities.Compliance;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-1. ComplianceProfile + ComplianceFieldRule round-trip against
/// the real schema (FK, unique industry key), and the industry-profile seeder is idempotent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ComplianceProfileSchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Profile_with_field_rule_round_trips()
    {
        await using var db = fixture.CreateContext();
        await db.ComplianceFieldRules.ExecuteDeleteAsync();
        await db.ComplianceProfiles.ExecuteDeleteAsync();

        var profile = new ComplianceProfile
        {
            IndustryKey = "firearms",
            Name = "Firearms",
            RequiredTraceabilityType = TraceabilityType.Serial,
            SdsRequired = false,
            IsActive = true,
            IsSystem = true,
        };
        profile.FieldRules.Add(new ComplianceFieldRule { FieldKey = "serialNumber", ProcessStep = "part.create" });
        db.ComplianceProfiles.Add(profile);
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var loaded = await verify.ComplianceProfiles.Include(p => p.FieldRules)
            .SingleAsync(p => p.IndustryKey == "firearms");
        loaded.RequiredTraceabilityType.Should().Be(TraceabilityType.Serial);
        loaded.FieldRules.Should().ContainSingle().Which.ProcessStep.Should().Be("part.create");
    }

    [Fact]
    public async Task Seeder_creates_industry_profiles_idempotently()
    {
        await using var db = fixture.CreateContext();
        await db.ComplianceFieldRules.ExecuteDeleteAsync();
        await db.ComplianceProfiles.ExecuteDeleteAsync();

        await SeedData.SeedComplianceProfilesAsync(db);
        await SeedData.SeedComplianceProfilesAsync(db);

        await using var verify = fixture.CreateContext();
        (await verify.ComplianceProfiles.CountAsync()).Should().Be(3);
        var food = await verify.ComplianceProfiles.SingleAsync(p => p.IndustryKey == "food");
        food.RequiredTraceabilityType.Should().Be(TraceabilityType.Lot);
        food.SdsRequired.Should().BeTrue();
        food.IsActive.Should().BeFalse("seeded profiles start inactive");
    }
}
