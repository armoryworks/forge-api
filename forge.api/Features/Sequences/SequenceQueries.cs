using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;

namespace Forge.Api.Features.Sequences;

/// <summary>Shared include graphs so every handler loads the same shape.</summary>
public static class SequenceQueries
{
    public static IQueryable<SequenceDefinition> WithGraph(this IQueryable<SequenceDefinition> q) =>
        q.Include(d => d.Steps).Include(d => d.Edges).Include(d => d.Gates);

    public static IQueryable<SequenceInstance> WithGraph(this IQueryable<SequenceInstance> q) =>
        q.Include(i => i.Definition!).ThenInclude(d => d.Steps)
         .Include(i => i.Definition!).ThenInclude(d => d.Edges)
         .Include(i => i.Definition!).ThenInclude(d => d.Gates)
         .Include(i => i.Steps)
         .Include(i => i.Gates);

    /// <summary>Activity-log indexing points for a run: the instance itself and, when present, its subject.</summary>
    public static (string, int)[] IndexingPoints(SequenceInstance i) =>
        i.SubjectEntityType is not null && i.SubjectEntityId.HasValue
            ? [("SequenceInstance", i.Id), (i.SubjectEntityType, i.SubjectEntityId.Value)]
            : [("SequenceInstance", i.Id)];
}
