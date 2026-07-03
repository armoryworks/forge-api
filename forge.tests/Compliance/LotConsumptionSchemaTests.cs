using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-2. A LotConsumption genealogy edge (consumed → produced lot)
/// round-trips against the real schema, enabling forward/backward recall traversal.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LotConsumptionSchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Consumption_edge_links_two_lots()
    {
        await using var db = fixture.CreateContext();
        await db.LotConsumptions.ExecuteDeleteAsync();

        var part = new Part
        {
            PartNumber = $"LC-{Guid.NewGuid():N}"[..16],
            Name = "Genealogy Test",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var raw = new LotRecord { LotNumber = "RAW-1", PartId = part.Id, Quantity = 100 };
        var finished = new LotRecord { LotNumber = "FIN-1", PartId = part.Id, Quantity = 10 };
        db.LotRecords.AddRange(raw, finished);
        await db.SaveChangesAsync();

        db.LotConsumptions.Add(new LotConsumption
        {
            ConsumedLotId = raw.Id,
            ProducedLotId = finished.Id,
            Quantity = 5.5m,
        });
        await db.SaveChangesAsync();

        await using var verify = fixture.CreateContext();
        // Backward trace: what went into the finished lot?
        var inputs = await verify.LotConsumptions
            .Where(c => c.ProducedLotId == finished.Id)
            .Include(c => c.ConsumedLot)
            .ToListAsync();
        inputs.Should().ContainSingle();
        inputs[0].ConsumedLot.LotNumber.Should().Be("RAW-1");
        inputs[0].Quantity.Should().Be(5.5m);
    }
}
