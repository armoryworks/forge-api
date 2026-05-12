using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record GetEcosQuery(EcoStatus? Status) : IRequest<List<EcoResponseModel>>;

public class GetEcosHandler(AppDbContext db) : IRequestHandler<GetEcosQuery, List<EcoResponseModel>>
{
    public async Task<List<EcoResponseModel>> Handle(GetEcosQuery request, CancellationToken cancellationToken)
    {
        var query = db.EngineeringChangeOrders.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(e => e.Status == request.Status.Value);

        var ecos = await query
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new
            {
                e.Id, e.EcoNumber, e.Title, e.Description, e.ChangeType, e.Status, e.Priority,
                e.ReasonForChange, e.ImpactAnalysis, e.EffectiveDate,
                e.RequestedById, e.ApprovedById, e.ApprovedAt, e.ImplementedAt,
                e.CreatedAt,
                AffectedItemCount = e.AffectedItems.Count,
            })
            .ToListAsync(cancellationToken);

        var userIds = ecos
            .Select(e => e.RequestedById)
            .Concat(ecos.Where(e => e.ApprovedById.HasValue).Select(e => e.ApprovedById!.Value))
            .Distinct()
            .ToList();

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return ecos.Select(e => new EcoResponseModel
        {
            Id = e.Id,
            EcoNumber = e.EcoNumber,
            Title = e.Title,
            Description = e.Description,
            ChangeType = e.ChangeType,
            Status = e.Status,
            Priority = e.Priority,
            ReasonForChange = e.ReasonForChange,
            ImpactAnalysis = e.ImpactAnalysis,
            EffectiveDate = e.EffectiveDate,
            RequestedById = e.RequestedById,
            RequestedByName = userNames.GetValueOrDefault(e.RequestedById, "Unknown"),
            ApprovedByName = e.ApprovedById.HasValue ? userNames.GetValueOrDefault(e.ApprovedById.Value) : null,
            ApprovedAt = e.ApprovedAt,
            ImplementedAt = e.ImplementedAt,
            AffectedItemCount = e.AffectedItemCount,
            CreatedAt = e.CreatedAt,
        }).ToList();
    }
}
