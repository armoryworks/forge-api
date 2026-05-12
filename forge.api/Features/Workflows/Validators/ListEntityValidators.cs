using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Validators;

/// <summary>
/// Workflow Pattern Phase 3 — List entity readiness validators, optionally
/// filtered by entityType. Used by both admin UI and the runtime page-load
/// fetch for a given entity type's surface.
/// </summary>
public record ListEntityValidatorsQuery(string? EntityType) : IRequest<IReadOnlyList<EntityValidatorResponseModel>>;

public class ListEntityValidatorsHandler(AppDbContext db)
    : IRequestHandler<ListEntityValidatorsQuery, IReadOnlyList<EntityValidatorResponseModel>>
{
    public async Task<IReadOnlyList<EntityValidatorResponseModel>> Handle(
        ListEntityValidatorsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.EntityReadinessValidators.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(v => v.EntityType == request.EntityType);

        var rows = await query
            .OrderBy(v => v.EntityType)
            .ThenBy(v => v.ValidatorId)
            .ToListAsync(cancellationToken);

        return rows.Select(v => new EntityValidatorResponseModel(
            v.Id,
            v.EntityType,
            v.ValidatorId,
            v.Predicate,
            v.DisplayNameKey,
            v.MissingMessageKey,
            v.IsSeedData)).ToList();
    }
}
