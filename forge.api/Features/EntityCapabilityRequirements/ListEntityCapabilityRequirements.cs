using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCapabilityRequirements;

/// <summary>
/// Admin list of entity-capability requirement rows, optionally filtered
/// by entityType / capabilityCode. Used by the
/// <c>/admin/entity-completeness</c> page.
/// </summary>
public record ListEntityCapabilityRequirementsQuery(string? EntityType, string? CapabilityCode)
    : IRequest<IReadOnlyList<EntityCapabilityRequirementResponseModel>>;

public class ListEntityCapabilityRequirementsHandler(AppDbContext db)
    : IRequestHandler<ListEntityCapabilityRequirementsQuery, IReadOnlyList<EntityCapabilityRequirementResponseModel>>
{
    public async Task<IReadOnlyList<EntityCapabilityRequirementResponseModel>> Handle(
        ListEntityCapabilityRequirementsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.EntityCapabilityRequirements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(r => r.EntityType == request.EntityType);
        if (!string.IsNullOrWhiteSpace(request.CapabilityCode))
            query = query.Where(r => r.CapabilityCode == request.CapabilityCode);

        var rows = await query
            .OrderBy(r => r.EntityType)
            .ThenBy(r => r.CapabilityCode)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.RequirementId)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new EntityCapabilityRequirementResponseModel(
            r.Id,
            r.EntityType,
            r.CapabilityCode,
            r.RequirementId,
            r.Predicate,
            r.DisplayNameKey,
            r.MissingMessageKey,
            r.SortOrder,
            r.IsSeedData)).ToList();
    }
}
