using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Compliance;

/// <summary>
/// regulated-parts-safety C-2: the write path that populates lot_consumptions
/// genealogy edges (previously missing). Verifies edge creation, idempotency, and
/// the self-reference + directed-cycle guards that keep the genealogy a DAG, plus
/// that GetLotTraceability now surfaces both trace directions.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RecordLotConsumptionHandlerTests(PostgresFixture fixture)
{
    private async Task<Part> SeedPartAsync(AppDbContext db)
    {
        var part = new Part
        {
            PartNumber = $"LCH-{Guid.NewGuid():N}"[..16],
            Name = "Genealogy Handler Test",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    private static async Task<LotRecord> SeedLotAsync(AppDbContext db, int partId, decimal qty)
    {
        var lot = new LotRecord { LotNumber = $"LOT-{Guid.NewGuid():N}"[..20], PartId = partId, Quantity = qty };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync();
        return lot;
    }

    private static RecordLotConsumptionCommand Consume(int producedId, params (int lotId, decimal qty)[] inputs) =>
        new(producedId, new RecordLotConsumptionRequestModel(
            inputs.Select(i => new LotConsumptionInputModel(i.lotId, i.qty)).ToList()));

    [Fact]
    public async Task Records_edges_and_returns_them()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw1 = await SeedLotAsync(db, part.Id, 100);
        var raw2 = await SeedLotAsync(db, part.Id, 50);
        var produced = await SeedLotAsync(db, part.Id, 40);

        var result = await new RecordLotConsumptionHandler(db)
            .Handle(Consume(produced.Id, (raw1.Id, 60), (raw2.Id, 30)), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(e => e.LotId).Should().BeEquivalentTo(new[] { raw1.Id, raw2.Id });
        result.Single(e => e.LotId == raw1.Id).Quantity.Should().Be(60);

        await using var verify = fixture.CreateContext();
        (await verify.LotConsumptions.CountAsync(c => c.ProducedLotId == produced.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Re_recording_the_same_pair_is_idempotent()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var produced = await SeedLotAsync(db, part.Id, 40);
        var handler = new RecordLotConsumptionHandler(db);

        await handler.Handle(Consume(produced.Id, (raw.Id, 60)), CancellationToken.None);
        var second = await handler.Handle(Consume(produced.Id, (raw.Id, 60)), CancellationToken.None);

        second.Should().ContainSingle();
        await using var verify = fixture.CreateContext();
        (await verify.LotConsumptions.CountAsync(c => c.ProducedLotId == produced.Id && c.ConsumedLotId == raw.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task Self_reference_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var lot = await SeedLotAsync(db, part.Id, 40);

        var act = () => new RecordLotConsumptionHandler(db)
            .Handle(Consume(lot.Id, (lot.Id, 1)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cycle_is_rejected()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var a = await SeedLotAsync(db, part.Id, 100);
        var b = await SeedLotAsync(db, part.Id, 40);
        var handler = new RecordLotConsumptionHandler(db);

        // a consumed into b (edge a -> b).
        await handler.Handle(Consume(b.Id, (a.Id, 10)), CancellationToken.None);

        // Now try b consumed into a — b is forward-reachable from a, so this closes a cycle.
        var act = () => handler.Handle(Consume(a.Id, (b.Id, 5)), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Missing_produced_lot_throws_not_found()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);

        var act = () => new RecordLotConsumptionHandler(db)
            .Handle(Consume(2_000_000_000, (raw.Id, 1)), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Traceability_surfaces_both_directions()
    {
        await using var db = fixture.CreateContext();
        var part = await SeedPartAsync(db);
        var raw = await SeedLotAsync(db, part.Id, 100);
        var produced = await SeedLotAsync(db, part.Id, 40);
        await new RecordLotConsumptionHandler(db)
            .Handle(Consume(produced.Id, (raw.Id, 60)), CancellationToken.None);

        await using var q = fixture.CreateContext();
        var traceHandler = new GetLotTraceabilityHandler(q);

        // Backward: the produced lot's inputs include the raw lot.
        var producedTrace = await traceHandler.Handle(new GetLotTraceabilityQuery(produced.LotNumber), CancellationToken.None);
        producedTrace.ConsumedLots.Should().ContainSingle(e => e.LotNumber == raw.LotNumber);
        producedTrace.ProducedLots.Should().BeEmpty();

        // Forward: the raw lot was consumed into the produced lot.
        var rawTrace = await traceHandler.Handle(new GetLotTraceabilityQuery(raw.LotNumber), CancellationToken.None);
        rawTrace.ProducedLots.Should().ContainSingle(e => e.LotNumber == produced.LotNumber);
        rawTrace.ConsumedLots.Should().BeEmpty();
    }
}
