using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record GetEcoByIdQuery(int Id) : IRequest<EcoResponseModel>;

public class GetEcoByIdHandler(AppDbContext db) : IRequestHandler<GetEcoByIdQuery, EcoResponseModel>
{
    public async Task<EcoResponseModel> Handle(GetEcoByIdQuery request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .AsNoTracking()
            .Include(e => e.AffectedItems)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        var userIds = new List<int> { eco.RequestedById };
        if (eco.ApprovedById.HasValue) userIds.Add(eco.ApprovedById.Value);

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return new EcoResponseModel
        {
            Id = eco.Id,
            EcoNumber = eco.EcoNumber,
            Title = eco.Title,
            Description = eco.Description,
            ChangeType = eco.ChangeType,
            Status = eco.Status,
            Priority = eco.Priority,
            ReasonForChange = eco.ReasonForChange,
            ImpactAnalysis = eco.ImpactAnalysis,
            EffectiveDate = eco.EffectiveDate,
            RequestedById = eco.RequestedById,
            RequestedByName = userNames.GetValueOrDefault(eco.RequestedById, "Unknown"),
            ApprovedByName = eco.ApprovedById.HasValue ? userNames.GetValueOrDefault(eco.ApprovedById.Value) : null,
            ApprovedAt = eco.ApprovedAt,
            ImplementedAt = eco.ImplementedAt,
            AffectedItemCount = eco.AffectedItems.Count,
            CreatedAt = eco.CreatedAt,
            AffectedItems = eco.AffectedItems.Select(a => new EcoAffectedItemResponseModel
            {
                Id = a.Id,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                ChangeDescription = a.ChangeDescription,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                IsImplemented = a.IsImplemented,
            }).ToList(),
        };
    }
}
