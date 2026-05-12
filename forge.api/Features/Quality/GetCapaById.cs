using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetCapaByIdQuery(int Id) : IRequest<CapaResponseModel>;

public class GetCapaByIdHandler(AppDbContext db)
    : IRequestHandler<GetCapaByIdQuery, CapaResponseModel>
{
    public async Task<CapaResponseModel> Handle(
        GetCapaByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await db.CorrectiveActions
            .AsNoTracking()
            .Include(x => x.Tasks)
            .Include(x => x.RelatedNcrs)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"CAPA {request.Id} not found");

        var userIds = new[]
            {
                c.OwnerId, c.ClosedById ?? 0, c.RootCauseAnalyzedById ?? 0,
                c.VerifiedById ?? 0, c.EffectivenessCheckedById ?? 0
            }
            .Where(id => id > 0).Distinct().ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return new CapaResponseModel
        {
            Id = c.Id,
            CapaNumber = c.CapaNumber,
            Type = c.Type,
            SourceType = c.SourceType,
            SourceEntityId = c.SourceEntityId,
            SourceEntityType = c.SourceEntityType,
            Title = c.Title,
            ProblemDescription = c.ProblemDescription,
            ImpactDescription = c.ImpactDescription,
            RootCauseAnalysis = c.RootCauseAnalysis,
            RootCauseMethod = c.RootCauseMethod,
            RootCauseMethodData = c.RootCauseMethodData,
            RootCauseAnalyzedById = c.RootCauseAnalyzedById,
            RootCauseAnalyzedByName = c.RootCauseAnalyzedById.HasValue ? userNames.GetValueOrDefault(c.RootCauseAnalyzedById.Value) : null,
            RootCauseCompletedAt = c.RootCauseCompletedAt,
            ContainmentAction = c.ContainmentAction,
            CorrectiveActionDescription = c.CorrectiveActionDescription,
            PreventiveAction = c.PreventiveAction,
            VerificationMethod = c.VerificationMethod,
            VerificationResult = c.VerificationResult,
            VerifiedById = c.VerifiedById,
            VerifiedByName = c.VerifiedById.HasValue ? userNames.GetValueOrDefault(c.VerifiedById.Value) : null,
            VerificationDate = c.VerificationDate,
            EffectivenessCheckDueDate = c.EffectivenessCheckDueDate,
            EffectivenessCheckDate = c.EffectivenessCheckDate,
            EffectivenessResult = c.EffectivenessResult,
            IsEffective = c.IsEffective,
            EffectivenessCheckedById = c.EffectivenessCheckedById,
            EffectivenessCheckedByName = c.EffectivenessCheckedById.HasValue ? userNames.GetValueOrDefault(c.EffectivenessCheckedById.Value) : null,
            OwnerId = c.OwnerId,
            OwnerName = userNames.GetValueOrDefault(c.OwnerId, "Unknown"),
            Status = c.Status,
            Priority = c.Priority,
            DueDate = c.DueDate,
            ClosedAt = c.ClosedAt,
            ClosedById = c.ClosedById,
            ClosedByName = c.ClosedById.HasValue ? userNames.GetValueOrDefault(c.ClosedById.Value) : null,
            TaskCount = c.Tasks.Count,
            CompletedTaskCount = c.Tasks.Count(t => t.Status == CapaTaskStatus.Completed),
            RelatedNcrCount = c.RelatedNcrs.Count,
            CreatedAt = c.CreatedAt,
        };
    }
}
