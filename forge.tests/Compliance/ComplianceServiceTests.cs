using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
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

    [Fact]
    public async Task Assembly_sds_aggregates_deduped_from_bom()
    {
        await using var db = fixture.CreateContext();
        await db.PartSafetyDataSheets.ExecuteDeleteAsync();
        await db.Set<BOMLine>().ExecuteDeleteAsync();

        Part MkPart(string n) => new()
        {
            PartNumber = $"{n}-{Guid.NewGuid():N}"[..16],
            Name = n,
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
        };
        var asm = MkPart("ASM");
        var c1 = MkPart("C1");
        var c2 = MkPart("C2");
        db.Parts.AddRange(asm, c1, c2);
        await db.SaveChangesAsync();
        db.Set<BOMLine>().AddRange(
            new BOMLine { ParentPartId = asm.Id, ChildPartId = c1.Id, Quantity = 1 },
            new BOMLine { ParentPartId = asm.Id, ChildPartId = c2.Id, Quantity = 2 });
        await db.SaveChangesAsync();

        var ds1 = new DocumentSet { Kind = "sds" };
        var ds2 = new DocumentSet { Kind = "sds" };
        db.Set<DocumentSet>().AddRange(ds1, ds2);
        await db.SaveChangesAsync();
        // c1 and c2 share ds1 (same material SDS) → collapses; c2 also has ds2.
        db.PartSafetyDataSheets.AddRange(
            new PartSafetyDataSheet { PartId = c1.Id, DocumentSetId = ds1.Id, SdsType = SdsType.Manufacturing },
            new PartSafetyDataSheet { PartId = c2.Id, DocumentSetId = ds1.Id, SdsType = SdsType.Manufacturing },
            new PartSafetyDataSheet { PartId = c2.Id, DocumentSetId = ds2.Id, SdsType = SdsType.Consumer });
        await db.SaveChangesAsync();

        var agg = await new ComplianceService(db).GetAssemblySdsAsync(asm.Id);
        agg.Select(s => s.DocumentSetId).Should().BeEquivalentTo([ds1.Id, ds2.Id]);
    }
}
