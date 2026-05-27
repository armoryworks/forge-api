using System.Security.Claims;
using FluentValidation;
using MediatR;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Parts;

public record CreateBOMEntryCommand(int ParentPartId, CreateBOMEntryRequestModel Data) : IRequest<PartDetailResponseModel>;

public class CreateBOMEntryCommandValidator : AbstractValidator<CreateBOMEntryCommand>
{
    public CreateBOMEntryCommandValidator()
    {
        RuleFor(x => x.ParentPartId).GreaterThan(0);
        RuleFor(x => x.Data.ChildPartId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
    }
}

public class CreateBOMEntryHandler(
    IPartRepository repo,
    IBomRevisionService bomRevisions,
    IHttpContextAccessor httpContext) : IRequestHandler<CreateBOMEntryCommand, PartDetailResponseModel>
{
    public async Task<PartDetailResponseModel> Handle(CreateBOMEntryCommand request, CancellationToken cancellationToken)
    {
        var parent = await repo.FindAsync(request.ParentPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.ParentPartId} not found");

        if (request.Data.ChildPartId == request.ParentPartId)
            throw new InvalidOperationException("A part cannot reference itself in its BOM");

        var child = await repo.FindAsync(request.Data.ChildPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Child part {request.Data.ChildPartId} not found");

        // D5: reject any edge that would close a BOM cycle (A→B→…→A), not just the
        // direct self-reference above. The new edge parent→child forms a cycle iff the
        // parent is already reachable beneath the child, so walk the child's descendants.
        if (await WouldCreateCycleAsync(repo, request.ParentPartId, request.Data.ChildPartId, cancellationToken))
            throw new InvalidOperationException(
                "Adding this component would create a BOM cycle (this parent already appears beneath the component).");

        var maxSort = await repo.GetMaxBomSortOrderAsync(request.ParentPartId, cancellationToken);

        var entry = new BOMEntry
        {
            ParentPartId = request.ParentPartId,
            ChildPartId = request.Data.ChildPartId,
            Quantity = request.Data.Quantity,
            ReferenceDesignator = request.Data.ReferenceDesignator?.Trim(),
            SortOrder = maxSort + 1,
            SourceType = request.Data.SourceType,
            LeadTimeDays = request.Data.LeadTimeDays,
            Notes = request.Data.Notes?.Trim(),
        };

        await repo.AddBomEntryAsync(entry, cancellationToken);

        // Phase 3 H4 / WU-20 — adding a component is a structural change;
        // capture an immutable revision snapshot of the new state.
        var userId = TryGetUserId(httpContext);
        await bomRevisions.CaptureCurrentStateAsync(request.ParentPartId, userId, "Component added", cancellationToken);

        return (await repo.GetDetailAsync(request.ParentPartId, cancellationToken))!;
    }

    private static int? TryGetUserId(IHttpContextAccessor http)
    {
        var raw = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var v) ? v : (int?)null;
    }

    /// <summary>
    /// BFS the descendant tree of <paramref name="childId"/> over BOM child-edges.
    /// If <paramref name="parentId"/> is reachable, the proposed parent→child edge
    /// would close a cycle. <c>visited</c> also makes this safe against any
    /// pre-existing cycle in the data.
    /// </summary>
    private static async Task<bool> WouldCreateCycleAsync(
        IPartRepository repo, int parentId, int childId, CancellationToken ct)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(childId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == parentId) return true;
            if (!visited.Add(current)) continue;

            foreach (var grandChildId in await repo.GetBomChildIdsAsync(current, ct))
                queue.Enqueue(grandChildId);
        }

        return false;
    }
}
