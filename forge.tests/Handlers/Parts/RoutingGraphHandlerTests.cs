using System.Reflection;

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class RoutingGraphHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly PartRepository _repo;

    public RoutingGraphHandlerTests()
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
        var ops = (steps.Length == 0 ? [10, 20, 30, 40] : steps)
            .Select(s => new Operation { PartId = part.Id, StepNumber = s, Title = $"Step {s}" })
            .ToList();
        _db.Operations.AddRange(ops);
        await _db.SaveChangesAsync();
        return (part, ops);
    }

    private Task<RoutingGraphResponseModel> GetAsync(Part part)
        => new GetRoutingGraphHandler(_repo).Handle(new GetRoutingGraphQuery(part.Id), CancellationToken.None);

    private Task<RoutingGraphResponseModel> SaveAsync(Part part, string? version, params RoutingGraphStepRequestModel[] steps)
        => new UpdateRoutingGraphHandler(_db, _repo).Handle(
            new UpdateRoutingGraphCommand(part.Id, new UpdateRoutingGraphRequestModel(version, [.. steps])), CancellationToken.None);

    private static RoutingGraphStepRequestModel Step(Operation op, int stepNumber, params (Operation Op, PredecessorGate Gate)[] follows)
        => new(op.Id, stepNumber, follows.Select(f => new OperationPredecessorRequestModel(f.Op.Id, f.Gate)).ToList());

    private static (Operation, PredecessorGate) Q(Operation op) => (op, PredecessorGate.Quantity);

    private async Task<List<(int Op, int Pred, PredecessorGate Gate)>> LinksAsync()
        => (await _db.OperationPredecessors.AsNoTracking().ToListAsync())
            .Select(l => (l.OperationId, l.PredecessorOperationId, l.Gate))
            .ToList();

    [Fact]
    public async Task Saving_a_diamond_then_reading_it_back_gives_the_same_graph()
    {
        var (part, ops) = await SeedAsync();
        var before = await GetAsync(part);

        var saved = await SaveAsync(part, before.Version,
            Step(ops[0], 10),
            Step(ops[1], 20, Q(ops[0])),
            Step(ops[2], 30, Q(ops[0])),
            Step(ops[3], 40, Q(ops[1]), (ops[2], PredecessorGate.Complete)));
        var reloaded = await GetAsync(part);

        reloaded.Should().BeEquivalentTo(saved);
        reloaded.Version.Should().NotBe(before.Version);
        reloaded.Operations.Single(o => o.Id == ops[3].Id).Predecessors.Should().BeEquivalentTo(new List<OperationPredecessorResponseModel>
        {
            new(ops[1].Id, 20, PredecessorGate.Quantity),
            new(ops[2].Id, 30, PredecessorGate.Complete),
        });
        reloaded.Operations.Single(o => o.Id == ops[2].Id).Predecessors.Should().ContainSingle()
            .Which.OperationId.Should().Be(ops[0].Id);
        (await _db.ActivityLogs.CountAsync(a => a.EntityType == "Part" && a.EntityId == part.Id && a.Action == "routing-graph-saved"))
            .Should().Be(1);
    }

    [Fact]
    public async Task A_reorder_and_the_links_it_needs_save_together()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);

        var saved = await SaveAsync(part, null,
            Step(ops[0], 10),
            Step(ops[2], 20, Q(ops[0])),
            Step(ops[1], 30, Q(ops[2])));

        saved.Operations.OrderBy(o => o.StepNumber).Select(o => o.Id).Should().Equal(ops[0].Id, ops[2].Id, ops[1].Id);
        (await LinksAsync()).Should().BeEquivalentTo(new[]
        {
            (ops[2].Id, ops[0].Id, PredecessorGate.Quantity),
            (ops[1].Id, ops[2].Id, PredecessorGate.Quantity),
        });
        var activity = await _db.ActivityLogs.SingleAsync(a => a.Action == "routing-graph-saved");
        activity.Description.Should().Be("Saved the routing flow: 2 steps renumbered, 2 links added");
    }

    [Fact]
    public async Task A_graph_that_breaks_the_rules_is_refused_whole_and_nothing_is_written()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);

        var act = () => SaveAsync(part, null,
            Step(ops[0], 10),
            Step(ops[1], 30, Q(ops[2])),
            Step(ops[2], 20, Q(ops[1])));

        var thrown = await act.Should().ThrowAsync<RoutingGraphInvalidException>();
        thrown.Which.Issues.Should().ContainSingle().Which.Should().Be(new RoutingGraphIssueResponseModel(
            RoutingGraphIssueKind.PredecessorNotEarlier, ops[2].Id, 20, ops[1].Id, 30));
        (await _db.Operations.AsNoTracking().OrderBy(o => o.Id).Select(o => o.StepNumber).ToListAsync()).Should().Equal(10, 20, 30);
        (await LinksAsync()).Should().BeEmpty();
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "routing-graph-saved")).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_step_is_reported_with_its_current_step_number()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);

        var act = () => SaveAsync(part, null, Step(ops[0], 10), Step(ops[1], 20));

        (await act.Should().ThrowAsync<RoutingGraphInvalidException>()).Which.Issues.Should().ContainSingle()
            .Which.Should().Be(new RoutingGraphIssueResponseModel(RoutingGraphIssueKind.MissingStep, ops[2].Id, 30, null, null));
    }

    [Fact]
    public async Task A_save_based_on_an_old_version_is_refused_with_the_current_version()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);
        var opened = await GetAsync(part);
        await SaveAsync(part, opened.Version, Step(ops[0], 10), Step(ops[1], 20, Q(ops[0])), Step(ops[2], 30, Q(ops[0])));

        var act = () => SaveAsync(part, opened.Version, Step(ops[0], 10), Step(ops[1], 20), Step(ops[2], 30));

        var thrown = await act.Should().ThrowAsync<RoutingGraphStaleException>();
        thrown.Which.CurrentVersion.Should().Be((await GetAsync(part)).Version);
        (await LinksAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Saving_the_graph_unchanged_writes_nothing()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);
        var opened = await GetAsync(part);

        var saved = await SaveAsync(part, opened.Version, Step(ops[0], 10), Step(ops[1], 20), Step(ops[2], 30));

        saved.Version.Should().Be(opened.Version);
        (await _db.ActivityLogs.AnyAsync(a => a.Action == "routing-graph-saved")).Should().BeFalse();
    }

    [Fact]
    public async Task Links_left_out_are_removed_and_a_changed_gate_is_kept_on_the_same_link()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);
        await SaveAsync(part, null, Step(ops[0], 10), Step(ops[1], 20, Q(ops[0])), Step(ops[2], 30, Q(ops[0]), Q(ops[1])));

        await SaveAsync(part, null, Step(ops[0], 10), Step(ops[1], 20), Step(ops[2], 30, (ops[1], PredecessorGate.Complete)));

        (await LinksAsync()).Should().BeEquivalentTo(new[] { (ops[2].Id, ops[1].Id, PredecessorGate.Complete) });
        (await _db.ActivityLogs.Where(a => a.Action == "routing-graph-saved").Select(a => a.Description).ToListAsync())
            .Should().Contain("Saved the routing flow: 2 links removed, 1 link's gate changed");
    }

    [Fact]
    public async Task A_deleted_step_is_not_part_of_the_graph()
    {
        var (part, ops) = await SeedAsync(10, 20, 30);
        ops[1].DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await SaveAsync(part, null, Step(ops[0], 10), Step(ops[2], 20, Q(ops[0])));

        saved.Operations.Select(o => o.Id).Should().BeEquivalentTo([ops[0].Id, ops[2].Id]);
        var act = () => SaveAsync(part, null, Step(ops[0], 10), Step(ops[1], 15), Step(ops[2], 20));
        (await act.Should().ThrowAsync<RoutingGraphInvalidException>()).Which.Issues.Should().ContainSingle()
            .Which.Kind.Should().Be(RoutingGraphIssueKind.UnknownStep);
    }

    [Fact]
    public async Task An_unknown_part_is_not_found()
    {
        var act = () => new UpdateRoutingGraphHandler(_db, _repo).Handle(
            new UpdateRoutingGraphCommand(999_999, new UpdateRoutingGraphRequestModel(null, [])), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void The_validator_refuses_an_unknown_gate_and_too_many_steps()
    {
        var validator = new UpdateRoutingGraphValidator();
        var badGate = new UpdateRoutingGraphCommand(1, new UpdateRoutingGraphRequestModel(null,
            [new RoutingGraphStepRequestModel(1, 10, [new OperationPredecessorRequestModel(2, (PredecessorGate)9)])]));
        var tooMany = new UpdateRoutingGraphCommand(1, new UpdateRoutingGraphRequestModel(null,
            Enumerable.Range(1, UpdateRoutingGraphValidator.MaxSteps + 1).Select(i => new RoutingGraphStepRequestModel(i, i, null)).ToList()));

        validator.Validate(badGate).IsValid.Should().BeFalse();
        validator.Validate(tooMany).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Only_admins_managers_and_engineers_can_save_the_graph()
    {
        var roles = typeof(PartsController).GetMethod(nameof(PartsController.UpdateRoutingGraph))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles;

        roles.Should().Be("Admin,Manager,Engineer");
    }
}
