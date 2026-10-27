using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

/// <summary>
/// The whole-routing save renumbers steps and rewrites links in one transaction under an advisory lock, against
/// the real primary key and foreign keys of operation_predecessors. Only a real Postgres proves the round trip.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RoutingGraphPostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_graph_saved_in_one_request_reads_back_the_same_from_a_new_context()
    {
        int partId;
        List<int> ids;
        await using (var seed = fixture.CreateContext())
        {
            var part = new Part
            {
                PartNumber = $"RG-{Guid.NewGuid():N}"[..16],
                Name = "Routing graph round trip",
                ProcurementSource = ProcurementSource.Make,
                InventoryClass = InventoryClass.Component,
            };
            seed.Parts.Add(part);
            await seed.SaveChangesAsync();
            var ops = new[] { 10, 20, 30, 40 }
                .Select(s => new Operation { PartId = part.Id, StepNumber = s, Title = $"Step {s}" })
                .ToList();
            seed.Operations.AddRange(ops);
            await seed.SaveChangesAsync();
            seed.OperationPredecessors.Add(new OperationPredecessor { OperationId = ops[3].Id, PredecessorOperationId = ops[2].Id });
            await seed.SaveChangesAsync();
            partId = part.Id;
            ids = ops.Select(o => o.Id).ToList();
        }

        RoutingGraphResponseModel saved;
        await using (var db = fixture.CreateContext())
        {
            var repo = new PartRepository(db, Mock.Of<IPartPricingResolver>());
            var opened = await new GetRoutingGraphHandler(repo).Handle(new GetRoutingGraphQuery(partId), CancellationToken.None);
            saved = await new UpdateRoutingGraphHandler(db, repo).Handle(new UpdateRoutingGraphCommand(partId,
                new UpdateRoutingGraphRequestModel(opened.Version,
                [
                    new(ids[0], 10, null),
                    new(ids[2], 20, [new(ids[0])]),
                    new(ids[1], 30, [new(ids[0])]),
                    new(ids[3], 40, [new(ids[1]), new(ids[2], PredecessorGate.Complete)]),
                ])), CancellationToken.None);
        }

        await using (var read = fixture.CreateContext())
        {
            var reloaded = await new GetRoutingGraphHandler(new PartRepository(read, Mock.Of<IPartPricingResolver>()))
                .Handle(new GetRoutingGraphQuery(partId), CancellationToken.None);

            reloaded.Version.Should().Be(saved.Version);
            reloaded.Operations.Select(o => (o.Id, o.StepNumber)).Should().Equal((ids[0], 10), (ids[2], 20), (ids[1], 30), (ids[3], 40));
            reloaded.Operations[3].Predecessors.Should().BeEquivalentTo(new List<OperationPredecessorResponseModel>
            {
                new(ids[1], 30, PredecessorGate.Quantity),
                new(ids[2], 20, PredecessorGate.Complete),
            });
            (await read.OperationPredecessors.CountAsync(l => ids.Contains(l.OperationId))).Should().Be(4);
        }
    }

    [Fact]
    public async Task A_list_reorder_after_a_flowchart_save_is_refused_as_stale_and_changes_nothing()
    {
        int partId;
        List<int> ids;
        await using (var seed = fixture.CreateContext())
        {
            var part = new Part
            {
                PartNumber = $"RS-{Guid.NewGuid():N}"[..16],
                Name = "Routing stale reorder",
                ProcurementSource = ProcurementSource.Make,
                InventoryClass = InventoryClass.Component,
            };
            seed.Parts.Add(part);
            await seed.SaveChangesAsync();
            var ops = new[] { 10, 20, 30 }
                .Select(s => new Operation { PartId = part.Id, StepNumber = s, Title = $"Step {s}" })
                .ToList();
            seed.Operations.AddRange(ops);
            await seed.SaveChangesAsync();
            partId = part.Id;
            ids = ops.Select(o => o.Id).ToList();
        }

        string listOpened;
        await using (var db = fixture.CreateContext())
        {
            var repo = new PartRepository(db, Mock.Of<IPartPricingResolver>());
            listOpened = (await new GetRoutingGraphHandler(repo).Handle(new GetRoutingGraphQuery(partId), CancellationToken.None)).Version;
            await new UpdateRoutingGraphHandler(db, repo).Handle(new UpdateRoutingGraphCommand(partId,
                new UpdateRoutingGraphRequestModel(listOpened,
                [
                    new(ids[0], 10, null),
                    new(ids[1], 20, [new(ids[0])]),
                    new(ids[2], 30, [new(ids[0])]),
                ])), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var repo = new PartRepository(db, Mock.Of<IPartPricingResolver>());
            var act = () => new ReorderOperationsHandler(repo, db).Handle(new ReorderOperationsCommand(partId,
                new ReorderOperationsRequestModel([ids[2], ids[1], ids[0]], RoutingVersion: listOpened)), CancellationToken.None);
            await act.Should().ThrowAsync<RoutingGraphStaleException>();
        }

        await using (var read = fixture.CreateContext())
        {
            (await read.Operations.AsNoTracking().Where(o => o.PartId == partId).OrderBy(o => o.StepNumber).Select(o => o.Id).ToListAsync())
                .Should().Equal(ids);
            (await read.OperationPredecessors.CountAsync(l => ids.Contains(l.OperationId))).Should().Be(2);
        }
    }
}
