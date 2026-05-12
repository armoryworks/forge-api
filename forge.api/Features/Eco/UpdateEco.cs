using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record UpdateEcoCommand(int Id, UpdateEcoRequestModel Request) : IRequest<EcoResponseModel>;

public class UpdateEcoHandler(AppDbContext db) : IRequestHandler<UpdateEcoCommand, EcoResponseModel>
{
    public async Task<EcoResponseModel> Handle(UpdateEcoCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .Include(e => e.AffectedItems)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        var r = request.Request;
        if (r.Title is not null) eco.Title = r.Title;
        if (r.Description is not null) eco.Description = r.Description;
        if (r.ChangeType.HasValue) eco.ChangeType = r.ChangeType.Value;
        if (r.Priority.HasValue) eco.Priority = r.Priority.Value;
        if (r.ReasonForChange is not null) eco.ReasonForChange = r.ReasonForChange;
        if (r.ImpactAnalysis is not null) eco.ImpactAnalysis = r.ImpactAnalysis;
        if (r.EffectiveDate.HasValue) eco.EffectiveDate = r.EffectiveDate.Value;

        await db.SaveChangesAsync(cancellationToken);

        var userNames = await db.Users.AsNoTracking()
            .Where(u => u.Id == eco.RequestedById || u.Id == eco.ApprovedById)
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
        };
    }
}
