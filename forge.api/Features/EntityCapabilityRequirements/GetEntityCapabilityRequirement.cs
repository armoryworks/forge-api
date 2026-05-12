using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EntityCapabilityRequirements;

/// <summary>Single-row admin lookup. 404 on miss.</summary>
public record GetEntityCapabilityRequirementQuery(int Id)
    : IRequest<EntityCapabilityRequirementResponseModel>;

public class GetEntityCapabilityRequirementHandler(AppDbContext db)
    : IRequestHandler<GetEntityCapabilityRequirementQuery, EntityCapabilityRequirementResponseModel>
{
    public async Task<EntityCapabilityRequirementResponseModel> Handle(
        GetEntityCapabilityRequirementQuery request,
        CancellationToken cancellationToken)
    {
        var row = await db.EntityCapabilityRequirements.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EntityCapabilityRequirement {request.Id} not found");

        return new EntityCapabilityRequirementResponseModel(
            row.Id, row.EntityType, row.CapabilityCode, row.RequirementId,
            row.Predicate, row.DisplayNameKey, row.MissingMessageKey,
            row.SortOrder, row.IsSeedData);
    }
}
