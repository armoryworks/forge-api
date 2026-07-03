using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities.Compliance;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-1. Effective requirements are the additive union of ACTIVE profiles:
/// strictest traceability, SDS-required if any, merged field rules; inactive profiles excluded.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ComplianceServiceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Effective_requirements_union_active_profiles_only()
    {
        await using var db = fixture.CreateContext();
        await db.ComplianceFieldRules.ExecuteDeleteAsync();
        await db.ComplianceProfiles.ExecuteDeleteAsync();

        var firearms = new ComplianceProfile { IndustryKey = "firearms", Name = "F", RequiredTraceabilityType = TraceabilityType.Serial, SdsRequired = false, IsActive = true };
        firearms.FieldRules.Add(new ComplianceFieldRule { FieldKey = "serialNumber", ProcessStep = "part.create" });
        var food = new ComplianceProfile { IndustryKey = "food", Name = "Food", RequiredTraceabilityType = TraceabilityType.Lot, SdsRequired = true, IsActive = true };
        food.FieldRules.Add(new ComplianceFieldRule { FieldKey = "lotNumber", ProcessStep = "part.create" });
        var inactive = new ComplianceProfile { IndustryKey = "medical", Name = "Med", RequiredTraceabilityType = TraceabilityType.Serial, SdsRequired = true, IsActive = false };
        inactive.FieldRules.Add(new ComplianceFieldRule { FieldKey = "udi", ProcessStep = "part.create" });
        db.ComplianceProfiles.AddRange(firearms, food, inactive);
        await db.SaveChangesAsync();

        var svc = new ComplianceService(db);
        var eff = await svc.GetEffectiveRequirementsAsync();

        eff.RequiredTraceabilityType.Should().Be(TraceabilityType.Serial, "strictest of active profiles");
        eff.SdsRequired.Should().BeTrue();
        eff.RequiredFields.Select(f => f.FieldKey).Should().BeEquivalentTo(["serialNumber", "lotNumber"]);

        var missing = await svc.GetMissingRequiredFieldsAsync("part.create", new HashSet<string> { "serialNumber" });
        missing.Should().BeEquivalentTo(["lotNumber"]);
    }
}
