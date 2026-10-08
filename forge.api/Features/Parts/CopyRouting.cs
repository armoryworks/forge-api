using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

/// <summary>
/// Copies another part's routing onto a part that has none yet. Material links follow the
/// component: each source BOM line is matched, in sort order, to a target BOM line with the same
/// child part, and a link whose component is not on the target's BOM is left out. Refused when the
/// target already has operations or the source has none.
/// </summary>
public record CopyRoutingCommand(int TargetPartId, int SourcePartId) : IRequest<List<OperationResponseModel>>;

public class CopyRoutingCommandValidator : AbstractValidator<CopyRoutingCommand>
{
    public CopyRoutingCommandValidator()
    {
        RuleFor(x => x.TargetPartId).GreaterThan(0);
        RuleFor(x => x.SourcePartId).GreaterThan(0)
            .NotEqual(x => x.TargetPartId).WithMessage("A part cannot copy its own routing.");
    }
}

public class CopyRoutingHandler(AppDbContext db, IPartRepository repo)
    : IRequestHandler<CopyRoutingCommand, List<OperationResponseModel>>
{
    public async Task<List<OperationResponseModel>> Handle(CopyRoutingCommand request, CancellationToken cancellationToken)
    {
        var target = await db.Parts.FirstOrDefaultAsync(p => p.Id == request.TargetPartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.TargetPartId} not found");
        var source = await db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.SourcePartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.SourcePartId} not found");

        if (await db.Operations.AnyAsync(o => o.PartId == target.Id, cancellationToken))
            throw new InvalidOperationException(
                $"{target.PartNumber} already has a routing. Remove its operations before copying another part's routing.");

        if (!await db.Operations.AnyAsync(o => o.PartId == source.Id, cancellationToken))
            throw new InvalidOperationException($"{source.PartNumber} has no routing to copy.");

        var bomMap = await MatchBomLinesAsync(source.Id, target.Id, cancellationToken);
        var operationCount = await RoutingCopier.CopyAsync(db, source.Id, target, bomMap, cancellationToken);

        db.LogActivityAt(
            "routing-copied",
            $"Copied {operationCount} operations from {source.PartNumber}",
            ("Part", target.Id));
        await db.SaveChangesAsync(cancellationToken);

        return await repo.GetOperationsAsync(target.Id, cancellationToken);
    }

    private async Task<Dictionary<int, BOMLine>> MatchBomLinesAsync(int sourcePartId, int targetPartId, CancellationToken ct)
    {
        var sourceLines = await db.BOMLines.AsNoTracking()
            .Where(b => b.ParentPartId == sourcePartId)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .ToListAsync(ct);
        var targetLines = await db.BOMLines
            .Where(b => b.ParentPartId == targetPartId)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .ToListAsync(ct);

        var available = targetLines
            .GroupBy(b => b.ChildPartId)
            .ToDictionary(g => g.Key, g => new Queue<BOMLine>(g));

        var map = new Dictionary<int, BOMLine>();
        foreach (var line in sourceLines)
        {
            if (available.TryGetValue(line.ChildPartId, out var candidates) && candidates.TryDequeue(out var match))
                map[line.Id] = match;
        }

        return map;
    }
}
