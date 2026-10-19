using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Inventory;

public record ApplyAbcClassificationCommand(int RunId) : IRequest;

public class ApplyAbcClassificationHandler(AppDbContext db) : IRequestHandler<ApplyAbcClassificationCommand>
{
    public async Task Handle(ApplyAbcClassificationCommand request, CancellationToken cancellationToken)
    {
        var runExists = await db.AbcClassificationRuns
            .AsNoTracking()
            .AnyAsync(r => r.Id == request.RunId, cancellationToken);
        if (!runExists)
            throw new KeyNotFoundException($"ABC classification run {request.RunId} not found");

        var classes = await db.AbcClassifications
            .AsNoTracking()
            .Where(c => c.RunId == request.RunId)
            .ToDictionaryAsync(c => c.PartId, c => c.Classification, cancellationToken);

        var partIds = classes.Keys.ToList();
        var parts = await db.Parts
            .Where(p => partIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var part in parts)
        {
            var newClass = classes[part.Id];
            if (part.AbcClass == newClass)
                continue;

            var previous = part.AbcClass?.ToString() ?? "none";
            part.AbcClass = newClass;
            db.LogActivityAt("abc-class-changed", $"ABC class changed from {previous} to {newClass}", ("Part", part.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
