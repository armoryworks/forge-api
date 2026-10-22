using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class RoutingPredecessorHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly PartRepository _repo;

    public RoutingPredecessorHandlerTests()
    {
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(r => r.ResolveAsync(It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int partId, int? _, decimal? _, CancellationToken _) =>
                new ResolvedPartPrice(partId, 0m, "USD", PartPriceSource.Default, null, null));
        _repo = new PartRepository(_db, pricing.Object);
    }

    private async Task<(Part Part, List<Operation> Ops)> SeedAsync(params int[] steps)
    {
        var part = new Part
        {
            PartNumber = $"P-{Guid.NewGuid():N}"[..12],
            Name = "Housing",
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var ops = (steps.Length == 0 ? [10, 20, 30] : steps)
            .Select(s => new Operation { PartId = part.Id, StepNumber = s, Title = $"Step {s}" })
            .ToList();
        _db.Operations.AddRange(ops);
        await _db.SaveChangesAsync();
        return (part, ops);
    }

    private async Task LinkAsync(Operation op, Operation predecessor, PredecessorGate gate = PredecessorGate.Quantity)
    {
        _db.OperationPredecessors.Add(new OperationPredecessor { OperationId = op.Id, PredecessorOperationId = predecessor.Id, Gate = gate });
        await _db.SaveChangesAsync();
    }

    private async Task<List<(int Op, int Pred, PredecessorGate Gate)>> LinksAsync()
        => (await _db.OperationPredecessors.AsNoTracking().ToListAsync())
            .Select(l => (l.OperationId, l.PredecessorOperationId, l.Gate))
            .ToList();

    private UpdateOperationCommand Update(Part part, Operation op, UpdateOperationRequestModel data) => new(part.Id, op.Id, data);

    private static UpdateOperationRequestModel Patch(
        int? stepNumber = null, List<OperationPredecessorRequestModel>? predecessors = null, bool removeConflicting = false)
        => new(stepNumber, null, null, null, null, null, null, null,
            Predecessors: predecessors, RemoveConflictingPredecessors: removeConflicting);

    [Fact]
    public async Task Create_stores_the_steps_a_new_operation_must_follow()
    {
        var (part, ops) = await SeedAsync();

        var created = await new CreateOperationHandler(_repo, Mock.Of<IVendorRepository>()).Handle(
            new CreateOperationCommand(part.Id, new CreateOperationRequestModel(40, "Pack", null, null, null, false, null, null,
                CycleCountdown: true,
                Predecessors: [new(ops[1].Id), new(ops[2].Id, PredecessorGate.Complete)])),
            CancellationToken.None);

        created.CycleCountdown.Should().BeTrue();
        created.Predecessors.Should().BeEquivalentTo(new List<OperationPredecessorResponseModel>
        {
            new(ops[1].Id, 20, PredecessorGate.Quantity),
            new(ops[2].Id, 30, PredecessorGate.Complete),
        });
        (await _db.ActivityLogs.AnyAsync(a => a.EntityType == "Part" && a.Action == "routing-predecessors-changed")).Should().BeTrue();
    }

    [Fact]
    public async Task A_link_to_a_higher_step_or_to_itself_is_refused()
    {
        var (part, ops) = await SeedAsync();
        var handler = new UpdateOperationHandler(_repo, Mock.Of<IVendorRepository>());

        var higher = () => handler.Handle(Update(part, ops[1], Patch(predecessors: [new(ops[2].Id)])), CancellationToken.None);
        var self = () => handler.Handle(Update(part, ops[1], Patch(predecessors: [new(ops[1].Id)])), CancellationToken.None);

        await higher.Should().ThrowAsync<ValidationException>().WithMessage("*lower step number*");
        await self.Should().ThrowAsync<ValidationException>();
        (await LinksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Parallel_steps_follow_the_same_step_and_an_empty_list_goes_back_to_the_previous_step()
    {
        var (part, ops) = await SeedAsync(10, 20, 30, 40);
        var handler = new UpdateOperationHandler(_repo, Mock.Of<IVendorRepository>());

        await handler.Handle(Update(part, ops[2], Patch(predecessors: [new(ops[0].Id)])), CancellationToken.None);
        var join = await handler.Handle(
            Update(part, ops[3], Patch(predecessors: [new(ops[1].Id), new(ops[2].Id)])), CancellationToken.None);

        join.Predecessors!.Select(p => p.StepNumber).Should().Equal(20, 30);
        (await LinksAsync()).Should().BeEquivalentTo(new[]
        {
            (ops[2].Id, ops[0].Id, PredecessorGate.Quantity),
            (ops[3].Id, ops[1].Id, PredecessorGate.Quantity),
            (ops[3].Id, ops[2].Id, PredecessorGate.Quantity),
        });

        var cleared = await handler.Handle(Update(part, ops[2], Patch(predecessors: [])), CancellationToken.None);
        cleared.Predecessors.Should().BeEmpty();
        (await LinksAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_renumber_that_breaks_a_link_is_refused_until_the_link_is_removed()
    {
        var (part, ops) = await SeedAsync();
        await LinkAsync(ops[2], ops[1]);
        var handler = new UpdateOperationHandler(_repo, Mock.Of<IVendorRepository>());

        var act = () => handler.Handle(Update(part, ops[1], Patch(stepNumber: 35)), CancellationToken.None);

        var conflict = (await act.Should().ThrowAsync<PredecessorConflictException>()).Which;
        conflict.Conflicts.Should().Equal(new PredecessorConflictResponseModel(ops[2].Id, 30, ops[1].Id, 35));

        _db.ChangeTracker.Clear();
        var moved = await handler.Handle(Update(part, ops[1], Patch(stepNumber: 35, removeConflicting: true)), CancellationToken.None);

        moved.StepNumber.Should().Be(35);
        (await LinksAsync()).Should().BeEmpty();
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "routing-predecessors-removed")).Should().BeTrue();
    }

    [Fact]
    public async Task Reorder_keeps_the_step_numbers_and_reports_broken_links()
    {
        var (part, ops) = await SeedAsync();
        await LinkAsync(ops[2], ops[0]);
        var handler = new ReorderOperationsHandler(_repo);

        var act = () => handler.Handle(
            new ReorderOperationsCommand(part.Id, new ReorderOperationsRequestModel([ops[2].Id, ops[0].Id, ops[1].Id])),
            CancellationToken.None);
        (await act.Should().ThrowAsync<PredecessorConflictException>()).Which.Conflicts.Should().ContainSingle();

        _db.ChangeTracker.Clear();
        var reordered = await handler.Handle(
            new ReorderOperationsCommand(part.Id, new ReorderOperationsRequestModel([ops[1].Id, ops[0].Id, ops[2].Id])),
            CancellationToken.None);

        reordered.Select(o => (o.Id, o.StepNumber)).Should().Equal((ops[1].Id, 10), (ops[0].Id, 20), (ops[2].Id, 30));
        (await LinksAsync()).Should().ContainSingle("step 30 still follows the old step 10, now numbered 20");
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "routing-reordered")).Should().BeTrue();
    }

    [Fact]
    public async Task Reorder_needs_every_step_once()
    {
        var (part, ops) = await SeedAsync();

        var act = () => new ReorderOperationsHandler(_repo).Handle(
            new ReorderOperationsCommand(part.Id, new ReorderOperationsRequestModel([ops[0].Id, ops[1].Id])),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Deleting_a_step_moves_its_followers_onto_its_predecessors()
    {
        var (part, ops) = await SeedAsync(10, 20, 30, 40);
        await LinkAsync(ops[2], ops[0]);
        await LinkAsync(ops[3], ops[2], PredecessorGate.Complete);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

        await new DeleteOperationHandler(_repo, clock.Object).Handle(new DeleteOperationCommand(part.Id, ops[2].Id), CancellationToken.None);

        (await LinksAsync()).Should().Equal((ops[3].Id, ops[0].Id, PredecessorGate.Complete));
        (await _db.Operations.IgnoreQueryFilters().SingleAsync(o => o.Id == ops[2].Id)).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Copying_a_routing_remaps_its_links()
    {
        var (source, ops) = await SeedAsync();
        await LinkAsync(ops[2], ops[0], PredecessorGate.Complete);
        var target = new Part { PartNumber = "COPY-1", Name = "Copy", ProcurementSource = ProcurementSource.Make, Status = PartStatus.Active };
        _db.Parts.Add(target);
        await _db.SaveChangesAsync();

        await RoutingCopier.CopyAsync(_db, source.Id, target, new Dictionary<int, BOMLine>(), CancellationToken.None);
        await _db.SaveChangesAsync();

        var copies = await _db.Operations.AsNoTracking().Where(o => o.PartId == target.Id).ToDictionaryAsync(o => o.StepNumber, o => o.Id);
        (await LinksAsync()).Should().Contain((copies[30], copies[10], PredecessorGate.Complete));
        (await _db.OperationPredecessors.AsNoTracking().SingleAsync(l => l.OperationId == copies[30])).CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Part_step_gating_is_set_and_cleared_and_logged()
    {
        var (part, _) = await SeedAsync();
        var handler = new UpdatePartStepGatingHandler(_repo);

        await handler.Handle(new UpdatePartStepGatingCommand(part.Id, new UpdatePartStepGatingRequestModel(StepGatingMode.Block)), CancellationToken.None);
        (await _db.Parts.AsNoTracking().SingleAsync(p => p.Id == part.Id)).StepGating.Should().Be(StepGatingMode.Block);

        await handler.Handle(new UpdatePartStepGatingCommand(part.Id, new UpdatePartStepGatingRequestModel(null)), CancellationToken.None);
        (await _db.Parts.AsNoTracking().SingleAsync(p => p.Id == part.Id)).StepGating.Should().BeNull();

        (await _db.ActivityLogs.Where(a => a.Action == "step-gating-changed").Select(a => a.Description).ToListAsync())
            .Should().Equal("Step gating changed from the shop setting to Block", "Step gating changed from Block to the shop setting");
    }
}
