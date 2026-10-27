using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record ReorderOperationsCommand(int PartId, ReorderOperationsRequestModel Data) : IRequest<List<OperationResponseModel>>;

public class ReorderOperationsValidator : AbstractValidator<ReorderOperationsCommand>
{
    public ReorderOperationsValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Data.OperationIds).NotEmpty()
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each step can appear only once.");
        RuleFor(x => x.Data.RoutingVersion).MaximumLength(64);
    }
}

public class ReorderOperationsHandler(IPartRepository repo, AppDbContext? db = null)
    : IRequestHandler<ReorderOperationsCommand, List<OperationResponseModel>>
{
    public async Task<List<OperationResponseModel>> Handle(ReorderOperationsCommand request, CancellationToken cancellationToken)
    {
        _ = await repo.FindAsync(request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        await using var tx = await RoutingWriteGuard.BeginAsync(db, request.PartId, cancellationToken);
        await RoutingWriteGuard.EnsureCurrentAsync(repo, request.PartId, request.Data.RoutingVersion, cancellationToken);

        var routing = await repo.GetRoutingAsync(request.PartId, cancellationToken);
        var ids = request.Data.OperationIds;
        if (routing.Count != ids.Count || routing.Any(o => !ids.Contains(o.Id)))
            throw new ValidationException("Send every step of this routing exactly once.");

        var numbers = routing.Select(o => o.StepNumber).OrderBy(n => n).ToList();
        var byId = routing.ToDictionary(o => o.Id);
        var moved = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            var operation = byId[ids[i]];
            if (operation.StepNumber == numbers[i])
                continue;
            operation.StepNumber = numbers[i];
            moved++;
        }

        if (moved > 0)
        {
            var links = await repo.GetPredecessorLinksAsync(request.PartId, cancellationToken);
            RoutingPredecessorEditor.ResolveConflicts(
                repo, request.PartId, routing, links, request.Data.RemoveConflictingPredecessors);
            repo.LogPartActivity(request.PartId, "routing-reordered",
                "Reordered the routing: " + string.Join(", ", ids.Select(id => $"Op {byId[id].StepNumber} {byId[id].Title}")));
            await repo.SaveChangesAsync(cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

        return await repo.GetOperationsAsync(request.PartId, cancellationToken);
    }
}
