using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-3. A PartSafetyDataSheet links a Part to a versioned DocumentSet
/// with SDS metadata (round-trips against the real schema).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PartSafetyDataSheetSchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Sds_links_part_to_document_set()
    {
        await using var db = fixture.CreateContext();
        await db.PartSafetyDataSheets.ExecuteDeleteAsync();

        var part = new Part
        {
            PartNumber = $"SDS-{Guid.NewGuid():N}"[..16],
            Name = "Hazmat Part",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Raw,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var docSet = new DocumentSet { Kind = "sds" };
        db.Set<DocumentSet>().Add(docSet);
        await db.SaveChangesAsync();

        db.PartSafetyDataSheets.Add(new PartSafetyDataSheet
        {
            PartId = part.Id,
            DocumentSetId = docSet.Id,
            SdsType = SdsType.Manufacturing,
            Supplier = "Acme Chemicals",
        });
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        var sds = await verify.PartSafetyDataSheets.SingleAsync(s => s.PartId == part.Id);
        sds.SdsType.Should().Be(SdsType.Manufacturing);
        sds.Supplier.Should().Be("Acme Chemicals");
        sds.DocumentSetId.Should().Be(docSet.Id);
    }
}
