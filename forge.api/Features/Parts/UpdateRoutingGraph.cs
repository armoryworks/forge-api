using System.Globalization;

using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Routing;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public record UpdateRoutingGraphCommand(int PartId, UpdateRoutingGraphRequestModel Data) : IRequest<RoutingGraphResponseModel>;

public class UpdateRoutingGraphValidator : AbstractValidator<UpdateRoutingGraphCommand>
{
    public const int MaxSteps = 500;

    public UpdateRoutingGraphValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Data.Steps).NotNull()
            .Must(s => s.Count <= MaxSteps).WithMessage($"A routing can have at most {MaxSteps} steps.");
        RuleFor(x => x.Data.Version).MaximumLength(64);
        RuleForEach(x => x.Data.Steps).ChildRules(step =>
            step.RuleForEach(s => s.Predecessors).ChildRules(p => p.RuleFor(l => l.Gate).IsInEnum()));
    }
}

/// <summary>
/// Saves a whole routing graph in one transaction: every step's number and every "must follow" link. The graph is
/// checked as a whole first (<see cref="RoutingGraph.Validate"/>); any problem refuses the save with 422 and the
/// conflicting links. A version from an earlier read that no longer matches refuses it with 409, so two editors
/// can't overwrite each other. A step sent with no links follows the previous step number, as everywhere else.
/// </summary>
public class UpdateRoutingGraphHandler(AppDbContext db, IPartRepository repo)
    : IRequestHandler<UpdateRoutingGraphCommand, RoutingGraphResponseModel>
{
    public async Task<RoutingGraphResponseModel> Handle(UpdateRoutingGraphCommand request, CancellationToken cancellationToken)
    {
        _ = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.AcquireAdvisoryLockAsync("parts.routing", request.PartId.ToString(CultureInfo.InvariantCulture), cancellationToken);

        var current = await repo.GetOperationsAsync(request.PartId, cancellationToken);
        var version = RoutingGraphVersion.Of(current);
        if (request.Data.Version is { Length: > 0 } sent && sent != version)
            throw new RoutingGraphStaleException(version);

        var routing = (await repo.GetRoutingAsync(request.PartId, cancellationToken)).Where(o => o.DeletedAt == null).ToList();
        var requested = request.Data.Steps
            .Select(s => new RoutingGraphStep(s.OperationId, s.StepNumber,
                (s.Predecessors ?? []).Select(p => new RoutingLink(s.OperationId, p.OperationId, p.Gate)).ToList()))
            .ToList();

        var issues = RoutingGraph.Validate(routing.Select(o => o.Id).ToList(), requested, RoutingPredecessorEditor.MaxPredecessors);
        if (issues.Count > 0)
        {
            var currentStep = routing.ToDictionary(o => o.Id, o => o.StepNumber);
            throw new RoutingGraphInvalidException(issues
                .Select(i => new RoutingGraphIssueResponseModel(
                    i.Kind, i.OperationId, i.StepNumber ?? currentStep.GetValueOrDefault(i.OperationId),
                    i.PredecessorOperationId, i.PredecessorStepNumber))
                .ToList());
        }

        var byId = routing.ToDictionary(o => o.Id);
        var renumbered = 0;
        foreach (var step in requested)
        {
            var operation = byId[step.OperationId];
            if (operation.StepNumber == step.StepNumber)
                continue;
            operation.StepNumber = step.StepNumber;
            renumbered++;
        }

        var links = await repo.GetPredecessorLinksAsync(request.PartId, cancellationToken);
        var wanted = requested
            .SelectMany(s => s.Predecessors.DistinctBy(l => l.PredecessorOperationId))
            .ToDictionary(l => (l.OperationId, l.PredecessorOperationId));
        var (added, removed, regated) = (0, 0, 0);

        foreach (var link in links)
        {
            if (!wanted.TryGetValue((link.OperationId, link.PredecessorOperationId), out var want))
            {
                repo.RemovePredecessorLink(link);
                removed++;
                continue;
            }
            if (link.Gate != want.Gate)
            {
                link.Gate = want.Gate;
                regated++;
            }
        }

        var existing = links.Select(l => (l.OperationId, l.PredecessorOperationId)).ToHashSet();
        foreach (var want in wanted.Values.Where(w => !existing.Contains((w.OperationId, w.PredecessorOperationId))))
        {
            repo.AddPredecessorLink(new OperationPredecessor
            {
                OperationId = want.OperationId,
                PredecessorOperationId = want.PredecessorOperationId,
                Gate = want.Gate,
            });
            added++;
        }

        if (renumbered + added + removed + regated == 0)
            return new RoutingGraphResponseModel(version, current);

        repo.LogPartActivity(request.PartId, "routing-graph-saved", Describe(renumbered, added, removed, regated));
        await repo.SaveChangesAsync(cancellationToken);
        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        var operations = await repo.GetOperationsAsync(request.PartId, cancellationToken);
        return new RoutingGraphResponseModel(RoutingGraphVersion.Of(operations), operations);
    }

    private static string Describe(int renumbered, int added, int removed, int regated)
    {
        var parts = new List<string>();
        if (renumbered > 0) parts.Add(renumbered == 1 ? "1 step renumbered" : $"{renumbered} steps renumbered");
        if (added > 0) parts.Add(added == 1 ? "1 link added" : $"{added} links added");
        if (removed > 0) parts.Add(removed == 1 ? "1 link removed" : $"{removed} links removed");
        if (regated > 0) parts.Add(regated == 1 ? "1 link's gate changed" : $"{regated} links' gates changed");
        return "Saved the routing flow: " + string.Join(", ", parts);
    }
}
